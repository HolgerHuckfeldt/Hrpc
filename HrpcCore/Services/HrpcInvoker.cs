using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using HrpcCore.Attributes;
using HrpcCore.Model.Messages;
using System.Net;
using HrpcCore.Model.Handler;
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

        private readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };


        public HrpcInvoker(IServiceScopeFactory serviceScopeFactory, ILogger<HrpcInvoker> logger)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _discoveryService = new HrpcDiscoveryService(_routeHandlers);
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
                _invokeCache.TryAdd(key, new InvokeInfo()
                {
                    MethodInfo = methodInfo,
                    AuthoriseAttribute = GetAttribute<HrpcAuthoriseAttribute>(methodInfo)
                });
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
            switch (mode)
            {
                case ExecutionMode.Parallel:
                    return await ExecuteInParallel(requests, context, ct);

                case ExecutionMode.Serial:
                    return await ExecuteSequentially(requests.ToList(), context, ct);

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public async Task<HrpcResponse?> InvokeDi(HrpcRequest request, HttpContext? context, CancellationToken ct = default)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogTrace("Starting RPC call {Controller}.{Method} with request ID {RequestId}", request.Controller, request.Method, request.Id);

            HrpcResponse response;
            try
            {
                response = await ExecuteSingleInvocationSimple(request, context, ct);
                _logger.LogDebug("RPC call {Controller}.{Method} completed. Status Code: {Code}", request.Controller, request.Method, response.Code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RPC call {Controller}.{Method} failed.", request.Controller, request.Method);
                response = InternalServerError($"Fehler in der Ausführung: {ex.Message}");
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

                var parameters = BuildParameters(parameterJson, invokeInfo.MethodInfo.GetParameters(), ct);
                if (parameters.Items == null) return parameters.Response;

                var result = await ExecuteMethod(invokeInfo.MethodInfo, controllerType, parameters.Items, ct);
                if (result != null)
                    result.Id = request?.Id ?? String.Empty;
                return result;
            }
            catch (Exception ex)
            {
                return InternalServerError($"Fehler in der Ausführung: {ex.Message}");
            }
        }


        private async Task<IEnumerable<HrpcResponse>> ExecuteInParallel(
            IEnumerable<HrpcRequest> requests,
            HttpContext? context,
            CancellationToken ct)
        {
            var tasks = requests.Select(request =>
                ExecuteSingleInvocation(request, context, ct));
            return await Task.WhenAll(tasks);
        }

        private async Task<IEnumerable<HrpcResponse>> ExecuteSequentially(
            List<HrpcRequest> requests,
            HttpContext? context,
            CancellationToken ct)
        {
            //var sortedRequests = SortRequestsByDependencies(requests);
            var responses = new ConcurrentDictionary<string, HrpcResponse?>();

            foreach (var current in requests)
            {
                // Verwende den neuen Mechanismus zum Auflösen von Abhängigkeiten
                var parametersJson = await ResolveParameterValues(current, responses);

                var effectiveRequest = new HrpcRequest()
                {
                    Id = current.Id,
                    Controller = current.Controller,
                    Method = current.Method,
                    StringData = parametersJson,
                    DependencyMapping = current.DependencyMapping
                };

                var result = await ExecuteSingleInvocation(effectiveRequest, context, ct);
                responses.TryAdd(current?.Id ?? String.Empty, result);
            }

            return responses.Values;
        }


        /// <summary>
        /// Aktualisiert die Parameter-JSON, indem Platzhalter (@dependency) ersetzt werden.
        /// Hier wird die JsonDependencyReplacer-Klasse verwendet.
        /// </summary>
        private async Task<string?> ResolveParameterValues(
            HrpcRequest current,
            ConcurrentDictionary<string, HrpcResponse> previousResponses)
        {
            var parameterJson = current.StringData;
            if (string.IsNullOrWhiteSpace(parameterJson))
                return parameterJson;

            // Parse die Parameter-JSON
            JsonNode? parameterNode = JsonNode.Parse(parameterJson);

            // Überprüfe, ob irgendwo ein String-Wert mit '@' vorkommt
            if (parameterNode != null && ContainsAlias(parameterNode))
            {
                // Alle ExposedDependencies aus den bisherigen Responses zusammenführen
                var mergedDependencies = new Dictionary<string, string>();
                foreach (var response in previousResponses.Values)
                {
                    if (response.ExposedDependencies != null)
                    {
                        foreach (var kv in response.ExposedDependencies)
                        {
                            // Hier kannst du auch Konfliktlösung oder Priorisierung einbauen
                            mergedDependencies[kv.Key] = kv.Value;
                        }
                    }
                }
                // Führe den Ersetzungsvorgang durch:
                ReplaceDependencyByAlias(parameterNode, mergedDependencies);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = null // Beibehaltung der Originalschreibweise
                };
                return parameterNode.ToJsonString(options);
            }

            return parameterJson;
        }

        /// <summary>
        /// Durchläuft den JsonNode rekursiv und gibt zurück, ob ein String-Wert gefunden wurde, der mit '@' beginnt.
        /// </summary>
        private bool ContainsAlias(JsonNode node)
        {
            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<string>(out var strValue))
                {
                    return strValue.Trim().StartsWith("@");
                }
                return false;
            }
            else if (node is JsonObject jsonObj)
            {
                foreach (var kvp in jsonObj)
                {
                    if (ContainsAlias(kvp.Value))
                        return true;
                }
                return false;
            }
            else if (node is JsonArray jsonArr)
            {
                foreach (var item in jsonArr)
                {
                    if (ContainsAlias(item))
                        return true;
                }
                return false;
            }
            return false;
        }


        #endregion

        #region Hilfsmethoden für die Ausführung

        private void ReplaceDependencyByAlias(JsonNode node, Dictionary<string, string>? exposedDependencies)
        {
            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<string>(out var strValue) && strValue.StartsWith("@"))
                {
                    string alias = strValue.Substring(1); // z. B. "firstId"
                    if (exposedDependencies != null && exposedDependencies.TryGetValue(alias, out var actualValue))
                    {
                        // Ersetze den aktuellen Knoten durch den extrahierten Wert (als JsonValue)
                        var newNode = JsonValue.Create(actualValue);
                        ReplaceInParent(node, newNode);
                    }
                }
            }
            else if (node is JsonObject jsonObj)
            {
                foreach (var property in jsonObj.ToList())
                {
                    ReplaceDependencyByAlias(property.Value, exposedDependencies);
                }
            }
            else if (node is JsonArray jsonArr)
            {
                for (int i = 0; i < jsonArr.Count; i++)
                {
                    ReplaceDependencyByAlias(jsonArr[i], exposedDependencies);
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

                var parameters = BuildParameters(request.StringData, invokeInfo.MethodInfo.GetParameters(), ct);
                if (parameters == default) return parameters.Response;

                var result = await ExecuteMethod(invokeInfo.MethodInfo, controllerType, parameters.Items!, ct);
                if (result != null)
                    result.Id = request.Id;

                if (request.DependencyMapping != null && result.Data != null)
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
                return InternalServerError($"Fehler in der Ausführung: {ex.Message}");
            }
        }

        #endregion

        #region Parameter-Bau und Validierung

        private (object?[] Items, HrpcResponse Response) BuildParameters(
            string? jsonInput,
            ParameterInfo[] methodParams,
            CancellationToken ct)
        {
            try
            {
                Dictionary<string, JsonElement>? parameterDict = null;
                if (!string.IsNullOrEmpty(jsonInput))
                {
                    // Deserialisiere in ein Dictionary<string, JsonElement>
                    parameterDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonInput, _jsonSerializerOptions);
                }

                var parameters = new object?[methodParams.Length];
                foreach (var param in methodParams)
                {
                    if (param.ParameterType == typeof(CancellationToken))
                    {
                        parameters[param.Position] = ct;
                    }
                    else
                    {
                        string name = param.Name ?? $"param{param.Position}";
                        if (parameterDict != null && parameterDict.TryGetValue(name, out JsonElement jsonValue))
                        {
                            // Nutze GetRawText(), um den ursprünglichen JSON-String zu erhalten, und deserialisiere dann in den gewünschten Typ
                            parameters[param.Position] = JsonSerializer.Deserialize(jsonValue.GetRawText(), param.ParameterType, _jsonSerializerOptions);
                        }
                        else
                        {
                            parameters[param.Position] = GetDefault(param.ParameterType);
                        }
                    }
                }
                return (parameters, null);
            }
            catch (Exception ex)
            {
                return (null, BadRequest($"Fehler bei der Parameterverarbeitung: {ex.Message}"));
            }
        }

        /// <summary>
        /// Gibt den Default-Wert für einen Typ zurück.
        /// </summary>
        private object? GetDefault(Type type)
        {
            if (type.IsValueType)
                return Activator.CreateInstance(type);
            return null;
        }

        #endregion

        #region Methoden-Ausführung und Reflection-Handling

        private async Task<HrpcResponse?> ExecuteMethod(
            MethodInfo methodInfo,
            Type controllerType,
            object?[]? parameters,
            CancellationToken ct)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var instance = scope.ServiceProvider.GetService(controllerType);

            if (instance == null)
                return InternalServerError($"Konnte {controllerType.Name} nicht aus dem DI-Container laden.");

            try
            {
                var result = methodInfo.Invoke(instance, parameters);
                if (result is Task taskResult)
                {
                    await taskResult;
                    // Versuche, das Result-Property zu extrahieren, wenn vorhanden.
                    var taskType = taskResult.GetType();
                    var resultProperty = taskType.GetProperty("Result");
                    if (resultProperty != null)
                    {
                        var resultValue = resultProperty.GetValue(taskResult);
                        return ReturnResponse(resultValue, instance?.GetType());
                    }
                    return Ok($"Vorgang abgeschlossen ({methodInfo.Name})");
                }
                else
                {
                    return ReturnResponse(result, controllerType);
                }
            }
            catch (TargetInvocationException tie)
            {
                return InternalServerError($"Fehler bei der Methode {tie.InnerException?.Message}");
            }
        }

        private object? GetTaskResult(Task taskResult)
        {
            var property = taskResult.GetType().GetProperty("Result");
            if (property != null && property.CanRead)
                return property.GetValue(taskResult);
            return default;
        }

        #endregion

        #region Hilfsmethoden & Response-Fabriken

        private static T? GetAttribute<T>(MemberInfo member) where T : Attribute
            => (T?)member.GetCustomAttributes(typeof(T), false).FirstOrDefault();

        private HrpcResponse ReturnResponse(object? result, Type instanceType)
        {
            if (result is null) return Ok(string.Empty);

            if (result is HrpcResponse hrpcResp)
                return hrpcResp;

            try { return Ok(JsonSerializer.Serialize(result)); }
            catch
            {
                var errorMessage = $"Kann das Ergebnis von '{instanceType.FullName}.{result.GetType().Name}' nicht serialisieren.";
                return InternalServerError(errorMessage);
            }
        }

        private Type? GetControllerType(string controllerName)
            => _routeHandlers.TryGetValue(controllerName, out var type) ? type : null;

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
            => new() { Code = (int)HttpStatusCode.Forbidden };

        private static HrpcResponse MethodNotAllowed()
            => new() { Code = (int)HttpStatusCode.MethodNotAllowed };

        private HrpcResponse CreateResponse(HttpStatusCode code, string? data)
        {
            return new HrpcResponse
            {
                Code = (int)code,
                Data = data
            };
        }

        #endregion





        public class InvokeInfo
        {
            public MethodInfo MethodInfo { get; set; } = null!;
            public HrpcAuthoriseAttribute? AuthoriseAttribute { get; set; }
        }
    }
}
