using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using HrpcCore.Attributes;
using System.Net;
using System.Text.Json.Nodes;
using HrpcCore.Model.Messages.Mex;
using HrpcCore.Services.Helper;
using System.Diagnostics;
using Microsoft.Extensions.Logging; // Für JsonPath.Net

namespace HrpcCore.Services
{
    public class HrpcInvoker : IHrpcCore
    {
        private readonly ConcurrentDictionary<string, Type> _routeHandlers = new();
        private readonly ConcurrentDictionary<string, InvokeInfo> _invokeCache = new();

        private readonly HrpcDiscoveryService _discoveryService;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly ILogger<HrpcInvoker> _logger;
        private readonly List<IHrpcInterceptor> _interceptors;

        private readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };


        public HrpcInvoker(IServiceScopeFactory serviceScopeFactory, ILogger<HrpcInvoker> logger, IEnumerable<IHrpcInterceptor>? interceptors = null)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _discoveryService = new HrpcDiscoveryService(_routeHandlers);
            _interceptors = interceptors?.ToList() ?? new List<IHrpcInterceptor>();
        }


        #region Registrierung von Controllern

        public void Register<T>()
        {
            Register(typeof(T));
        }

        public void Register(Type controllerType)
        {
            var controllerAttr = GetAttribute<HrpcControllerAttribute>(controllerType);
            if (controllerAttr == null) return;

            _routeHandlers.TryAdd(controllerAttr.Name, controllerType);

            foreach (var methodInfo in controllerType.GetMethods())
            {
                var methodAttr = GetAttribute<HrpcMethodAttribute>(methodInfo);
                if (methodAttr == null) continue;

                string key = $"{controllerAttr.Name}_{methodAttr.Name}";
                var invokeInfo = new InvokeInfo()
                {
                    MethodInfo = methodInfo,
                    AuthoriseAttribute = GetAttribute<HrpcAuthoriseAttribute>(methodInfo),
                    CompiledInvocation = CompileInvocation(controllerType, methodInfo),
                    IsAsync = typeof(Task).IsAssignableFrom(methodInfo.ReturnType),
                    HasResult = GetHasResult(methodInfo)
                };
                _invokeCache.TryAdd(key, invokeInfo);
            }
        }

        #endregion

        public DiscoveryInfo GetDiscoveryInfo()
        {
            var discovery = _discoveryService.GetDiscoveryInfo();
            return discovery;
        }

        #region Haupt-Invoke-Methoden

        public async Task<IEnumerable<HrpcResponse?>> InvokeDi(
            IEnumerable<HrpcRequest> requests,
            HttpContext? context,
            ExecutionMode mode = ExecutionMode.Parallel,
            CancellationToken ct = default)
        {
            var requestList = requests.ToList();

            // Auto-detect: if requests have DependencyMappings, use batch-based
            // topological execution regardless of the specified mode.
            if (requestList.Any(r => r.DependencyMapping != null && r.DependencyMapping.Count > 0))
            {
                return await ExecuteInDependencyBatches(requestList, context, ct);
            }

            switch (mode)
            {
                case ExecutionMode.Parallel:
                    return await ExecuteInParallel(requestList, context, ct);

                case ExecutionMode.Serial:
                    return await ExecuteSequentially(requestList, context, ct);

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public async Task<HrpcResponse?> InvokeDi(HrpcRequest request, HttpContext? context, CancellationToken ct = default)
        {
            // Build the interceptor pipeline wrapping the actual execution
            HrpcInvocationDelegate pipeline = (req, token) => ExecuteSingleInvocationSimple(req, context, token);

            // Wrap interceptors in reverse order (last interceptor runs first)
            for (int i = _interceptors.Count - 1; i >= 0; i--)
            {
                var interceptor = _interceptors[i];
                var next = pipeline;
                pipeline = (req, token) => interceptor.InvokeAsync(req, next, token);
            }

            var stopwatch = Stopwatch.StartNew();
            _logger.LogTrace("Starting RPC call {Controller}.{Method} with request ID {RequestId}", request.Controller, request.Method, request.Id);

            HrpcResponse? response;
            try
            {
                response = await pipeline(request, ct);
                _logger.LogDebug("RPC call {Controller}.{Method} completed. Status Code: {Code}", request.Controller, request.Method, response?.Code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RPC call {Controller}.{Method} failed.", request.Controller, request.Method);
                response = InternalServerError("An internal error occurred while processing the request.");
            }
            finally
            {
                stopwatch.Stop();
                _logger.LogTrace("RPC call {Controller}.{Method} took {Duration} ms", request.Controller, request.Method, stopwatch.ElapsedMilliseconds);
            }
            return response;
        }


        #endregion

        #region Ausführungs-Strategien

        private async Task<HrpcResponse?> ExecuteSingleInvocationSimple(
            HrpcRequest request,
            HttpContext? context,
            CancellationToken ct)
        {
            try
            {
                var controllerType = GetControllerType(request.Controller);
                if (controllerType == null)
                    return BadRequest($"Controller '{request.Controller}' nicht gefunden.", HttpStatusCode.NotFound);

                string key = $"{request.Controller}_{request.Method}";
                if (!_invokeCache.TryGetValue(key, out var invokeInfo))
                    return BadRequest($"Methode '{request.Method}' auf Controller '{request.Controller}' nicht gefunden.");

                try
                {
                    await CheckAuthorisation(invokeInfo.AuthoriseAttribute, context);

                }
                catch (UnauthorizedAccessException)
                {
                    return MethodNotAllowed();
                }

                // Im Single-Call ohne Dependencies brauchen wir keine Alias-Auflösung:
                string parameterJson = request?.StringData??String.Empty;

                var parameters = BuildParameters(parameterJson, invokeInfo.MethodInfo!.GetParameters(), ct);
                if (parameters.Items == null) return parameters.Response;

                // Inject binary data for byte[] parameters
                InjectBinaryParameters(parameters.Items!, invokeInfo.MethodInfo.GetParameters(), request?.BinaryData);

                var result = await ExecuteMethod(invokeInfo, controllerType, parameters.Items, ct);
                if (result != null)
                    result.Id = request?.Id ?? string.Empty;
                return result;
            }
            catch (Exception ex)
            {
                return InternalServerError("An internal error occurred while processing the request.");
            }
        }


        private async Task<IEnumerable<HrpcResponse?>> ExecuteInParallel(
            IEnumerable<HrpcRequest> requests,
            HttpContext? context,
            CancellationToken ct)
        {
            var tasks = requests.Select(request =>
                ExecuteSingleInvocation(request, context, ct));
            return await Task.WhenAll(tasks);
        }

        private async Task<IEnumerable<HrpcResponse?>> ExecuteSequentially(
            IEnumerable<HrpcRequest> requests,
            HttpContext? context,
            CancellationToken ct)
        {
            var responses = new ConcurrentDictionary<string, HrpcResponse?>();

            foreach (var current in requests)
            {
                // Verwende den neuen Mechanismus zum Auflösen von Abhängigkeiten
                var resolution = ResolveParameterValues(current, responses);
                if (resolution.Error != null)
                {
                    responses.TryAdd(current?.Id ?? String.Empty, resolution.Error);
                    continue;
                }

                var effectiveRequest = new HrpcRequest()
                {
                    Id = current.Id,
                    Controller = current.Controller,
                    Method = current.Method,
                    StringData = resolution.ResolvedJson,
                    DependencyMapping = current.DependencyMapping
                };

                var result = await ExecuteSingleInvocation(effectiveRequest, context, ct);
                responses.TryAdd(current?.Id ?? String.Empty, result);
            }

            return responses.Values;
        }


        /// <summary>
        /// Executes requests in topological batches: independent requests run in parallel,
        /// dependent requests wait for their dependencies to complete.
        /// </summary>
        private async Task<IEnumerable<HrpcResponse?>> ExecuteInDependencyBatches(
            List<HrpcRequest> requests,
            HttpContext? context,
            CancellationToken ct)
        {
            List<List<HrpcRequest>> batches;
            try
            {
                batches = DependencyGraphBuilder.SortByDependencyBatches(requests);
            }
            catch (InvalidOperationException ex)
            {
                // Cycle detected – return error for all requests
                return requests.Select(r => new HrpcResponse
                {
                    Code = (int)HttpStatusCode.BadRequest,
                    Data = "Circular dependency detected in request batch.",
                    Id = r.Id,
                    Error = new HrpcError { Code = 400, Message = "Circular dependency detected in request batch.", RequestId = r.Id }
                });
            }

            var allResponses = new List<HrpcResponse?>();

            foreach (var batch in batches)
            {
                var batchResponses = await Task.WhenAll(
                    batch.Select(request => ExecuteSingleInvocationWithDependencies(request, context, ct)));
                allResponses.AddRange(batchResponses);
            }

            return allResponses;
        }

        /// <summary>
        /// Executes a single invocation with dependency resolution from prior responses.
        /// </summary>
        private async Task<HrpcResponse?> ExecuteSingleInvocationWithDependencies(
            HrpcRequest request,
            HttpContext? context,
            CancellationToken ct)
        {
            return await ExecuteSingleInvocation(request, context, ct);
        }


        /// <summary>
        /// Löst Alias-Platzhalter (@alias) in den Parametern eines Requests anhand der
        /// ExposedDependencies vorheriger Responses auf. Gibt BadRequest zurück, wenn
        /// ein Alias nicht auflösbar ist.
        /// </summary>
        private (string? ResolvedJson, HrpcResponse? Error) ResolveParameterValues(
            HrpcRequest current,
            ConcurrentDictionary<string, HrpcResponse?>? previousResponses)
        {
            var parameterJson = current.StringData;
            if (string.IsNullOrWhiteSpace(parameterJson))
                return (parameterJson, null);

            // Parse die Parameter-JSON
            JsonNode? parameterNode;
            try
            {
                parameterNode = JsonNode.Parse(parameterJson);
            }
            catch (Exception ex)
            {
                return (null, BadRequest("Invalid parameter JSON format."));
            }

            if (parameterNode == null)
                return (parameterJson, null);

            if (!ContainsAlias(parameterNode))
                return (parameterJson, null);

            // Alle ExposedDependencies aus den bisherigen Responses zusammenführen
            var mergedDependencies = new Dictionary<string, string>(StringComparer.Ordinal);
            if (previousResponses != null)
            {
                foreach (var response in previousResponses.Values)
                {
                    if (response?.ExposedDependencies != null)
                    {
                        foreach (var kv in response.ExposedDependencies)
                        {
                            mergedDependencies[kv.Key] = kv.Value;
                        }
                    }
                }
            }

            // Führe den Ersetzungsvorgang durch:
            var unresolved = ReplaceDependencyByAlias(parameterNode, mergedDependencies);
            if (unresolved.Count > 0)
            {
                return (null, BadRequest($"Unaufgelöste Abhängigkeiten: {string.Join(", ", unresolved)}"));
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = null // Beibehaltung der Originalschreibweise
            };
            return (parameterNode.ToJsonString(options), null);
        }

        /// <summary>
        /// Durchläuft den JsonNode rekursiv und gibt zurück, ob ein String-Wert gefunden wurde, der mit '@' beginnt.
        /// </summary>
        private bool ContainsAlias(JsonNode? node)
        {
            if(node == null) return false;
            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<string>(out var strValue))
                {
                    return strValue.Trim().StartsWith("@"); //|| strValue.Trim().StartsWith("\"@");
                }
            }
            else if (node is JsonObject jsonObj)
            {
                foreach (var kvp in jsonObj)
                {
                    if (ContainsAlias(kvp.Value))
                        return true;
                }
            }
            else if (node is JsonArray jsonArr)
            {
                foreach (var item in jsonArr)
                {
                    if (ContainsAlias(item))
                        return true;
                }
            }
            return false;
        }


        #endregion

        #region Hilfsmethoden für die Ausführung

        private HashSet<string> ReplaceDependencyByAlias(JsonNode? node, Dictionary<string, string> exposedDependencies)
        {
            var unresolved = new HashSet<string>(StringComparer.Ordinal);
            ReplaceDependencyByAliasCore(node, exposedDependencies, unresolved);
            return unresolved;
        }

        private void ReplaceDependencyByAliasCore(
            JsonNode? node,
            Dictionary<string, string> exposedDependencies,
            HashSet<string> unresolved)
        {
            if (node == null) return;
            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<string>(out var strValue) && strValue.StartsWith("@"))
                {
                    string alias = strValue.Substring(1); // z. B. "firstId"
                    if (exposedDependencies.TryGetValue(alias, out var actualValue))
                    {
                        // Ersetze den aktuellen Knoten durch den extrahierten Wert (als JsonValue)
                        var newNode = JsonValue.Create(actualValue);
                        ReplaceInParent(node, newNode);
                    }
                    else
                    {
                        unresolved.Add(alias);
                    }
                }
            }
            else if (node is JsonObject jsonObj)
            {
                foreach (var property in jsonObj.ToList())
                {
                    if (property.Value != null)
                    {
                        ReplaceDependencyByAliasCore(property.Value, exposedDependencies, unresolved);
                    }
                }
            }
            else if (node is JsonArray jsonArr)
            {
                foreach (var t in jsonArr)
                {
                    ReplaceDependencyByAliasCore(t, exposedDependencies, unresolved);
                }
            }
        }

        private static void ReplaceInParent(JsonNode oldNode, JsonNode newNode)
        {
            // Erzeuge einen Klon des neuen Knotens, damit dieser keinen Elternbezug hat.
            var newNodeClone = JsonNode.Parse(newNode.ToJsonString());
            var parent = oldNode.Parent;
            if (parent is JsonObject parentObj)
            {
                // Durchlaufe alle Properties des Eltern-Objekts.
                foreach (var kvp in parentObj.ToList())
                {
                    if (kvp.Value == oldNode)
                    {
                        // Ersetze den alten Knoten durch den neuen Knoten.
                        parentObj[kvp.Key] = newNodeClone;
                        break;
                    }
                }
            }
            else if (parent is JsonArray parentArr)
            {
                // Durchlaufe das Array und ersetze den alten Knoten.
                for (int i = 0; i < parentArr.Count; i++)
                {
                    if (parentArr[i] == oldNode)
                    {
                        parentArr[i] = newNodeClone;
                        break;
                    }
                }
            }
            else
            {
                throw new InvalidOperationException("Knoten ohne Eltern können nicht ersetzt werden.");
            }
        }


        private async Task<HrpcResponse?> ExecuteSingleInvocation(
            HrpcRequest request,
            HttpContext? context,
            CancellationToken ct)
        {
            try
            {
                var controllerType = GetControllerType(request.Controller);
                if (controllerType == null)
                    return BadRequest($"Controller '{request.Controller}' nicht gefunden.", HttpStatusCode.NotFound);

                string key = $"{request.Controller}_{request.Method}";
                if (!_invokeCache.TryGetValue(key, out var invokeInfo))
                    return BadRequest($"Methode '{request.Method}' auf Controller '{request.Controller}' nicht gefunden.");

                try
                {
                    await CheckAuthorisation(invokeInfo.AuthoriseAttribute, context);
                }
                catch (UnauthorizedAccessException)
                {
                    return MethodNotAllowed();
                }

                var parameters = BuildParameters(request.StringData, invokeInfo.MethodInfo!.GetParameters(), ct);
                if (parameters == default) return parameters.Response;

                var result = await ExecuteMethod(invokeInfo, controllerType, parameters.Items!, ct);
                if (result != null)
                    result.Id = request.Id;

                if (request.DependencyMapping != null && result?.Data != null)
                {
                    var exposed = new Dictionary<string, string>();
                    // Verwenden Sie hier z. B. Ihre bestehende Hilfsklasse DependencyResolver
                    foreach (var kv in request.DependencyMapping)
                    {
                        string alias = kv.Key;
                        string jsonPath = kv.Value;
                        // Angenommen, ExtractValue gibt ein JsonNode oder string zurück
                        var extracted = DependencyResolver.ExtractValue(result.Data, jsonPath);
                        if (extracted != null)
                            exposed[alias] = extracted.ToString();
                    }
                    result.ExposedDependencies = exposed;
                }

                return result;
            }
            catch (Exception ex)
            {
                return InternalServerError("An internal error occurred while processing the request.");
            }
        }

        #endregion

        #region Parameter-Bau und Validierung

        private (object?[]? Items, HrpcResponse? Response) BuildParameters(
            string? jsonInput,
            ParameterInfo[] methodParams,
            CancellationToken ct)
        {
            try
            {
                List<HrpcParameter>? requestParams;
                if (!string.IsNullOrEmpty(jsonInput))
                {
                    requestParams = JsonSerializer.Deserialize<List<HrpcParameter>>(jsonInput, _jsonSerializerOptions);
                    if (requestParams == null || !requestParams.All(rp => rp.Data != null))
                        throw new FormatException("Ungültige JSON-Struktur.");
                }
                else
                {
                    requestParams = new List<HrpcParameter>();
                }

                // Erstelle ein Dictionary aus den übermittelten Parametern anhand ihres Namens
                var dict = requestParams.ToDictionary(rp => rp.ParameterName, rp => rp.Data);

                var parameters = new object?[methodParams.Length];
                foreach (var param in methodParams)
                {
                    if (param.ParameterType == typeof(CancellationToken))
                    {
                        parameters[param.Position] = ct;
                    }
                    else
                    {
                        // Verwende den Parameternamen zur Zuordnung
                        string name = param.Name ?? $"param{param.Position}";
                        if (dict.TryGetValue(name, out var jsonValue))
                        {
                            try
                            {
                                if (jsonValue != null)
                                {
                                    parameters[param.Position] =
                                        JsonSerializer.Deserialize(jsonValue, param.ParameterType,
                                            _jsonSerializerOptions);
                                }
                            }
                            catch (Exception ex)
                            {
                                return (null, BadRequest($"Parameter '{name}' cannot be converted to type '{param.ParameterType.Name}'."));
                            }
                        }
                        else
                        {
                            // Falls der Parameter fehlt, den Default-Wert verwenden
                            parameters[param.Position] = GetDefault(param.ParameterType);
                        }
                    }
                }
                return (parameters, null);
            }
            catch (Exception ex)
            {
                return (null, BadRequest("Error processing request parameters."));
            }
        }

        
        /// <summary>
        /// Injects binary data from HrpcRequest.BinaryData into byte[] parameters.
        /// If a method has a byte[] parameter, it receives the raw binary payload
        /// instead of a Base64-encoded JSON string.
        /// </summary>
        private static void InjectBinaryParameters(object?[] parameters, ParameterInfo[] methodParams, byte[]? binaryData)
        {
            if (binaryData == null || binaryData.Length == 0) return;

            // Find the first byte[] parameter and inject the binary data
            for (int i = 0; i < methodParams.Length; i++)
            {
                if (methodParams[i].ParameterType == typeof(byte[]))
                {
                    parameters[i] = binaryData;
                    break; // Only inject into the first byte[] parameter
                }
            }
        }
        private object? GetDefault(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }


        #endregion

        #region Methoden-Ausführung und Reflection-Handling

        private static bool GetHasResult(MethodInfo methodInfo)
        {
            var returnType = methodInfo.ReturnType;

            if (returnType == typeof(void))
                return false;

            // IAsyncEnumerable<T> always has results
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
                return true;

            if (typeof(Task).IsAssignableFrom(returnType))
            {
                return returnType.IsGenericType;
            }

            return true;
        }

        /// <summary>
        /// Returns true if the type is IAsyncEnumerable&lt;T&gt;.
        /// </summary>
        private static bool IsAsyncEnumerable(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
                return true;

            // Check if the type implements IAsyncEnumerable<T> (e.g. compiler-generated state machines)
            return type.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
        }

        /// <summary>
        /// Returns the element type of an IAsyncEnumerable&lt;T&gt;, or null if not applicable.
        /// </summary>
        private static Type? GetAsyncEnumerableElementType(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
                return type.GetGenericArguments()[0];

            // Check interfaces for compiler-generated types
            var iface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
            if (iface != null)
                return iface.GetGenericArguments()[0];

            // Also handle Task<IAsyncEnumerable<T>>
            if (typeof(Task).IsAssignableFrom(type) && type.IsGenericType)
            {
                var taskResultType = type.GetGenericArguments()[0];
                if (IsAsyncEnumerable(taskResultType))
                    return GetAsyncEnumerableElementType(taskResultType);
            }

            return null;
        }

        private static Func<object, object?[], object?> CompileInvocation(Type controllerType, MethodInfo methodInfo)
        {
            // Parameter: (object instance, object?[] args)
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var argsParam = Expression.Parameter(typeof(object?[]), "args");

            var parameters = methodInfo.GetParameters();
            var argExpressions = new Expression[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramInfo = parameters[i];
                // args[i]
                var indexed = Expression.ArrayIndex(argsParam, Expression.Constant(i));

                Expression converted;
                if (paramInfo.ParameterType == typeof(CancellationToken))
                {
                    // CancellationToken wird zur Laufzeit vom Invoker eingesetzt.
                    converted = Expression.Default(typeof(CancellationToken));
                }
                else
                {
                    converted = Expression.Convert(indexed, paramInfo.ParameterType);
                }

                argExpressions[i] = converted;
            }

            // ((ControllerType)instance).MethodName(...)
            var castInstance = Expression.Convert(instanceParam, controllerType);
            var call = Expression.Call(castInstance, methodInfo, argExpressions);

            Expression body;
            if (methodInfo.ReturnType == typeof(void))
            {
                body = Expression.Block(call, Expression.Constant(null, typeof(object)));
            }
            else if (methodInfo.ReturnType.IsValueType)
            {
                body = Expression.Convert(call, typeof(object));
            }
            else
            {
                body = Expression.TypeAs(call, typeof(object));
            }

            var lambda = Expression.Lambda<Func<object, object?[], object?>>(body, instanceParam, argsParam);
            return lambda.Compile();
        }

        private async Task<HrpcResponse?> ExecuteMethod(
            InvokeInfo invokeInfo,
            Type? controllerType,
            object?[]? parameters,
            CancellationToken ct)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            if (controllerType != null)
            {
                var instance = scope.ServiceProvider.GetService(controllerType);

                if (instance == null)
                    return InternalServerError($"Konnte {controllerType.Name} nicht aus dem DI-Container laden.");

                try
                {
                    var compiled = invokeInfo.CompiledInvocation;
                    if (compiled == null)
                    {
                        return InternalServerError($"Methode '{invokeInfo.MethodInfo?.Name}' ist nicht kompiliert.");
                    }

                    // Ersetze ggf. CancellationToken-Platzhalter durch den realen Wert
                    var methodParams = invokeInfo.MethodInfo!.GetParameters();
                    for (int i = 0; i < methodParams.Length; i++)
                    {
                        if (methodParams[i].ParameterType == typeof(CancellationToken))
                        {
                            parameters![i] = ct;
                        }
                    }

                    var result = compiled(instance, parameters!);

                    // Handle IAsyncEnumerable<T> directly (non-Task)
                    if (IsAsyncEnumerable(invokeInfo.MethodInfo!.ReturnType) && result != null)
                    {
                        var elementType = GetAsyncEnumerableElementType(invokeInfo.MethodInfo.ReturnType)!;
                        var list = await ConsumeAsyncEnumerable(result, elementType, ct);
                        return Ok(JsonSerializer.Serialize(list, _jsonSerializerOptions));
                    }

                    // Handle Task<IAsyncEnumerable<T>>
                    if (invokeInfo.IsAsync && result is Task taskResult)
                    {
                        await taskResult;

                        if (invokeInfo.HasResult)
                        {
                            var resultValue = GetTaskResult(taskResult);

                            // Check if the Task result is IAsyncEnumerable<T>
                            if (resultValue != null && IsAsyncEnumerable(resultValue.GetType()))
                            {
                                var elementType = GetAsyncEnumerableElementType(resultValue.GetType())!;
                                var list = await ConsumeAsyncEnumerable(resultValue, elementType, ct);
                                return Ok(JsonSerializer.Serialize(list, _jsonSerializerOptions));
                            }

                            return ReturnResponse(resultValue, instance?.GetType());
                        }

                        return Ok($"Vorgang abgeschlossen ({invokeInfo.MethodInfo.Name})");
                    }

                    return ReturnResponse(result, controllerType);
                }
                catch (TargetInvocationException tie)
                {
                    return InternalServerError($"Fehler bei der Methode {tie.InnerException?.Message}");
                }
            }
            else
            {
                return BadRequest("No Controller", HttpStatusCode.BadRequest);
            }
        }

        private object? GetTaskResult(Task taskResult)
        {
            if (taskResult is null)
                return null;

            // Für Task&lt;T&gt; greifen wir direkt über die generische Eigenschaft auf das Result zu.
            // Dies spart Reflection (GetProperty) pro Request.
            if (taskResult.GetType().IsGenericType)
            {
                return ((dynamic)taskResult).Result;
            }

            return null;
        }
        /// <summary>
        /// Consumes an IAsyncEnumerable<T> and collects all elements into a List<T>.
        /// Uses a generic helper method to avoid reflection issues with compiler-generated state machines.
        /// </summary>
        private static async Task<object> ConsumeAsyncEnumerable(object asyncEnumerable, Type elementType, CancellationToken ct)
        {
            var helperType = typeof(AsyncEnumerableConsumer<>).MakeGenericType(elementType);
            var method = helperType.GetMethod("Consume")!;
            var task = (Task)method.Invoke(null, new[] { asyncEnumerable, ct })!;
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }

        /// <summary>
        /// Generic helper class for consuming IAsyncEnumerable<T> without reflection.
        /// </summary>
        private static class AsyncEnumerableConsumer<T>
        {
            public static async Task<List<T>> Consume(IAsyncEnumerable<T> source, CancellationToken ct)
            {
                var list = new List<T>();
                await foreach (var item in source.WithCancellation(ct))
                {
                    list.Add(item);
                }
                return list;
            }
        }
        #endregion

        #region Hilfsmethoden & Response-Fabriken

        private static T? GetAttribute<T>(MemberInfo member) where T : Attribute
            => (T?)member.GetCustomAttributes(typeof(T), false).FirstOrDefault();

        private HrpcResponse ReturnResponse(object? result, Type? instanceType)
        {
            if (result is null) return Ok(string.Empty);

            if (result is HrpcResponse hrpcResp)
                return hrpcResp;

            // Binary return: put byte[] into Content instead of Base64-encoding in Data
            if (result is byte[] bytes)
            {
                var binResp = Ok(Convert.ToBase64String(bytes));
                binResp.Content = bytes;
                return binResp;
            }

            try { return Ok(JsonSerializer.Serialize(result)); }
            catch
            {
                _logger.LogWarning("Failed to serialize result of type {ResultType} from controller {ControllerType}",
                    result.GetType().Name, instanceType?.FullName);
                return InternalServerError("Failed to serialize the response.");
            }
        }

        private Type? GetControllerType(string controllerName)
            => _routeHandlers.GetValueOrDefault(controllerName);

        private async Task CheckAuthorisation(HrpcAuthoriseAttribute? authAttr, HttpContext? context)
        {
            if (authAttr != null && !await authAttr.OnAuthorization(context))
                throw new UnauthorizedAccessException();
        }

        #endregion

        #region Response-Generierung

        private HrpcResponse BadRequest(string message, HttpStatusCode code = HttpStatusCode.BadRequest)
            => CreateResponse(code, message);

        private HrpcResponse Ok(string? data)
            => CreateResponse(HttpStatusCode.OK, data);

        private HrpcResponse InternalServerError(string message)
            => CreateResponse(HttpStatusCode.InternalServerError, message);

        private static HrpcResponse Forbidden()
            => new() { Code = (int)HttpStatusCode.Forbidden, Error = new HrpcError { Code = 403, Message = "Forbidden" } };

        private static HrpcResponse MethodNotAllowed()
            => new() { Code = (int)HttpStatusCode.MethodNotAllowed, Error = new HrpcError { Code = 405, Message = "Method Not Allowed" } };

        private HrpcResponse CreateResponse(HttpStatusCode code, string? data)
        {
            var response = new HrpcResponse
            {
                Code = (int)code,
                Data = data
            };

            if ((int)code is < 200 or > 299)
            {
                response.Error = new HrpcError
                {
                    Code = (int)code,
                    Message = data ?? "Unknown error"
                };
            }

            return response;
        }

        #endregion

        public class InvokeInfo
        {
            public MethodInfo MethodInfo { get; set; } = null!;
            public HrpcAuthoriseAttribute? AuthoriseAttribute { get; set; }

            /// <summary>
            /// Kompilierter Delegate für den Methodenaufruf.
            /// Signatur: (object controllerInstance, object?[] args) => object?
            /// Falls die Methode async ist, wird der zurückgegebene Task noch nicht awaited.
            /// </summary>
            public Func<object, object?[], object?>? CompiledInvocation { get; set; }

            /// <summary>
            /// True, wenn die Methode asynchron (Task/Task&lt;T&gt;) ist.
            /// </summary>
            public bool IsAsync { get; set; }

            /// <summary>
            /// True, wenn die Methode einen Rückgabewert liefert (Task&lt;T&gt; oder synchroner Wert).
            /// </summary>
            public bool HasResult { get; set; }
        }
    }
}
