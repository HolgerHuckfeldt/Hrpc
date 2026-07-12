using System.Collections.Concurrent;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Encodings.Web;
using TrameCore.Attributes;
using System.Net;
using System.Text.Json.Nodes;
using TrameCore.Model.Messages.Mex;
using TrameCore.Services.Helper;
using System.Diagnostics;
using Microsoft.Extensions.Logging; // Für JsonPath.Net

namespace TrameCore.Services
{
    public class TrameInvoker : ITrameCore
    {
        private readonly ConcurrentDictionary<string, Type> _routeHandlers = new();
        private readonly ConcurrentDictionary<string, InvokeInfo> _invokeCache = new();

        private readonly TrameDiscoveryService _discoveryService;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly ILogger<TrameInvoker> _logger;
        private readonly List<ITrameInterceptor> _interceptors;

        /// <summary>
        /// Wenn gesetzt, werden in Fehler-Responses die echten Exception-Details
        /// (TrameError.Details) befüllt. In Produktion auf false belassen, um keine
        /// internen Informationen an Clients preiszugeben. Wird durch AddTrame anhand
        /// von TrameOptions.EnableDetailedErrors bzw. IHostEnvironment.IsDevelopment() gesetzt.
        /// </summary>
        public bool EnableDetailedErrors { get; set; }

        /// <summary>
        /// Maximale Elementzahl eines Top-Level-Array-/Collection-Parameters (Default 1000,
        /// 0 = unbegrenzt). Schützt den Server vor Kardinalitäts-Sprengung, insb. beim
        /// @alias-Whole-Collection-Passthrough (server-seitig erzeugte Arrays — Body-Size-Limits
        /// greifen nicht). string/byte[] ausgenommen. Wird über AddTrame aus
        /// TrameOptions gesetzt; der Invoker-Default schützt auch einen nackten `new TrameInvoker()`.
        /// </summary>
        public int MaxParameterArrayLength { get; set; } = 1000;

        /// <summary>
        /// Maximale Elementzahl eines Top-Level-Array-/Collection-Rückgabewerts (Default 10000,
        /// 0 = unbegrenzt). Verhindert, dass ein Einzelergebnis den Server in Memory treibt.
        /// Greift materialisierte Collections (via ReturnResponse) und IAsyncEnumerable-Streams
        /// (Early-Stop in ConsumeAsyncEnumerable). string/byte[] ausgenommen.
        /// </summary>
        public int MaxResultElementCount { get; set; } = 10000;

        // camelCase + relaxed Encoder: Der Payload wird jetzt strukturiert (JsonElement)
        // in Data abgelegt und von den Transporten in einem Pass roh serialisiert —
        // kein Double-Wrapping mehr. UnsafeRelaxed verhindert dennoch `"`-Escaping im
        // Mantel (z. B. in ExposedDependencies-Strings oder Fehlermeldungen).
        private readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };


        public TrameInvoker(IServiceScopeFactory serviceScopeFactory, ILogger<TrameInvoker> logger, IEnumerable<ITrameInterceptor>? interceptors = null)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _discoveryService = new TrameDiscoveryService(_routeHandlers);
            _interceptors = interceptors?.ToList() ?? new List<ITrameInterceptor>();
        }


        #region Registrierung von Controllern

        public void Register<T>()
        {
            Register(typeof(T));
        }

        public void Register(Type controllerType)
        {
            var controllerAttr = GetAttribute<TrameControllerAttribute>(controllerType);
            if (controllerAttr == null) return;

            _routeHandlers.TryAdd(controllerAttr.Name, controllerType);

            foreach (var methodInfo in controllerType.GetMethods())
            {
                var methodAttr = GetAttribute<TrameMethodAttribute>(methodInfo);
                if (methodAttr == null) continue;

                string key = $"{controllerAttr.Name}_{methodAttr.Name}";
                var invokeInfo = new InvokeInfo()
                {
                    MethodInfo = methodInfo,
                    AuthoriseAttribute = GetAttribute<TrameAuthoriseAttribute>(methodInfo),
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

        public async Task<IEnumerable<TrameResponse?>> InvokeDi(
            IEnumerable<TrameRequest> requests,
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

        public async Task<TrameResponse?> InvokeDi(TrameRequest request, HttpContext? context, CancellationToken ct = default)
        {
            // Build the interceptor pipeline wrapping the actual execution
            TrameInvocationDelegate pipeline = (req, token) => ExecuteSingleInvocationSimple(req, context, token);

            // Wrap interceptors in reverse order (last interceptor runs first)
            for (int i = _interceptors.Count - 1; i >= 0; i--)
            {
                var interceptor = _interceptors[i];
                var next = pipeline;
                pipeline = (req, token) => interceptor.InvokeAsync(req, next, token);
            }

            var stopwatch = Stopwatch.StartNew();
            _logger.LogTrace("Starting RPC call {Controller}.{Method} with request ID {RequestId}", request.Controller, request.Method, request.Id);

            TrameResponse? response;
            try
            {
                response = await pipeline(request, ct);
                _logger.LogDebug("RPC call {Controller}.{Method} completed. Status Code: {Code}", request.Controller, request.Method, response?.Code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RPC call {Controller}.{Method} failed.", request.Controller, request.Method);
                response = InternalServerError("An internal error occurred while processing the request.", ex);
            }
            finally
            {
                stopwatch.Stop();
                _logger.LogTrace("RPC call {Controller}.{Method} took {Duration} ms", request.Controller, request.Method, stopwatch.ElapsedMilliseconds);
            }
            // RequestId auf allen Pfaden durchreichen (auch Fehlerpfade).
            if (response != null && string.IsNullOrEmpty(response.Id))
                response.Id = request?.Id ?? string.Empty;
            return response;
        }


        #endregion

        #region Ausführungs-Strategien

        private async Task<TrameResponse?> ExecuteSingleInvocationSimple(
            TrameRequest request,
            HttpContext? context,
            CancellationToken ct)
        {
            try
            {
                var controllerType = GetControllerType(request.Controller);
                if (controllerType == null)
                    return BadRequest($"Controller '{request.Controller}' not found.", HttpStatusCode.NotFound);

                string key = $"{request.Controller}_{request.Method}";
                if (!_invokeCache.TryGetValue(key, out var invokeInfo))
                    return BadRequest($"Method '{request.Method}' not found on controller '{request.Controller}'.");

                try
                {
                    await CheckAuthorisation(invokeInfo.AuthoriseAttribute, context);

                }
                catch (UnauthorizedAccessException)
                {
                    return Unauthorized();
                }

                // Im Single-Call ohne Dependencies brauchen wir keine Alias-Auflösung:
                string parameterJson = request?.StringData ?? String.Empty;

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
                return InternalServerError("An internal error occurred while processing the request.", ex);
            }
        }


        private async Task<IEnumerable<TrameResponse?>> ExecuteInParallel(
            IEnumerable<TrameRequest> requests,
            HttpContext? context,
            CancellationToken ct)
        {
            var requestList = requests.ToList();
            var results = await Task.WhenAll(
                requestList.Select(request => ExecuteSingleInvocation(request, context, ct)));
            // RequestId durchreichen (Erfolgspfade setzen die Id bereits, Fehlerpfade nicht).
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] != null && string.IsNullOrEmpty(results[i]!.Id))
                    results[i]!.Id = requestList[i].Id ?? string.Empty;
            }
            return results;
        }

        private async Task<IEnumerable<TrameResponse?>> ExecuteSequentially(
            IEnumerable<TrameRequest> requests,
            HttpContext? context,
            CancellationToken ct)
        {
            var requestList = requests.ToList();
            // Lookup nach Id für die @alias-Auflösung innerhalb der Sequenz.
            var responses = new ConcurrentDictionary<string, TrameResponse?>();
            // Ergebnis in Request-Reihenfolge sammeln — ConcurrentDictionary.Values
            // bewahrt KEINE Einfügereihenfolge, weshalb Batch-Clients, die über das
            // erste Array-Element korrelieren (WebSocket/SignalR: root[0].Id), ohne
            // diese Ordnung keinen Match finden und bis ins Timeout hängen. Der
            // Parallel-Pfad (ExecuteInParallel) gibt results bereits index-genau in
            // Request-Reihenfolge zurück — dieses Verhalten muss Serial ebenfalls halten.
            var orderedResults = new List<TrameResponse?>(requestList.Count);

            foreach (var current in requestList)
            {
                // Verwende den neuen Mechanismus zum Auflösen von Abhängigkeiten
                var resolution = ResolveParameterValues(current, responses);
                if (resolution.Error != null)
                {
                    // Error-Response muss die Request-Id tragen, damit Clients den
                    // fehlerhaften Schritt korrelieren können — analog zum topologischen
                    // Pfad (ExecuteSingleInvocationWithDependencies setzt err.Id ebenfalls).
                    if (string.IsNullOrEmpty(resolution.Error.Id))
                        resolution.Error.Id = current?.Id ?? string.Empty;
                    responses.TryAdd(current?.Id ?? String.Empty, resolution.Error);
                    orderedResults.Add(resolution.Error);
                    continue;
                }

                var effectiveRequest = new TrameRequest()
                {
                    Id = current.Id,
                    Controller = current.Controller,
                    Method = current.Method,
                    StringData = resolution.ResolvedJson,
                    DependencyMapping = current.DependencyMapping
                };

                var result = await ExecuteSingleInvocation(effectiveRequest, context, ct);
                if (result != null && string.IsNullOrEmpty(result.Id))
                    result.Id = current?.Id ?? string.Empty;
                responses.TryAdd(current?.Id ?? String.Empty, result);
                orderedResults.Add(result);
            }

            return orderedResults;
        }


        /// <summary>
        /// Executes requests in topological batches: independent requests run in parallel,
        /// dependent requests wait for their dependencies to complete.
        /// </summary>
        private async Task<IEnumerable<TrameResponse?>> ExecuteInDependencyBatches(
            List<TrameRequest> requests,
            HttpContext? context,
            CancellationToken ct)
        {
            List<List<TrameRequest>> batches;
            try
            {
                batches = DependencyGraphBuilder.SortByDependencyBatches(requests);
            }
            catch (InvalidOperationException ex)
            {
                // Cycle detected – return error for all requests
                _logger.LogWarning(ex, "Circular dependency detected in request batch.");
                return requests.Select(r => new TrameResponse
                {
                    Code = (int)HttpStatusCode.BadRequest,
                    Id = r.Id,
                    Error = new TrameError { Code = 400, Message = "Circular dependency detected in request batch.", RequestId = r.Id }
                });
            }

            var allResponses = new List<TrameResponse?>();

            // Aufgetürmte Responses VORHERIGER Batches — abhängige Requests lesen
            // hieraus ihre @alias-Platzhalter (analog zu ExecuteSequentially). Innerhalb
            // eines Batches sind die Requests per Kahn unabhängig, daher darf ein Batch
            // nur die Responses früherer Batches sehen (die Writes erfolgen erst nach
            // Task.WhenAll, also ohne Race innerhalb des Batches).
            var priorResponses = new ConcurrentDictionary<string, TrameResponse?>();

            foreach (var batch in batches)
            {
                var batchResponses = await Task.WhenAll(
                    batch.Select(request => ExecuteSingleInvocationWithDependencies(
                        request, priorResponses, context, ct)));
                foreach (var response in batchResponses)
                {
                    allResponses.Add(response);
                    if (!string.IsNullOrEmpty(response?.Id))
                        priorResponses.TryAdd(response.Id, response);
                }
            }

            return allResponses;
        }

        /// <summary>
        /// Executes a single invocation with dependency resolution from prior responses.
        /// Löst @alias-Platzhalter anhand von priorResponses auf (sofern vorhanden)
        /// und baut einen effektiven Request mit ersetzten Parametern — identisch zum
        /// Mechanismus in ExecuteSequentially. Ohne priorResponses (Wurzel-Batch) wird
        /// der Request direkt ausgeführt.
        /// </summary>
        private async Task<TrameResponse?> ExecuteSingleInvocationWithDependencies(
            TrameRequest request,
            ConcurrentDictionary<string, TrameResponse?>? priorResponses,
            HttpContext? context,
            CancellationToken ct)
        {
            TrameRequest effective = request;
            if (priorResponses != null && !string.IsNullOrEmpty(request?.StringData))
            {
                var resolution = ResolveParameterValues(request, priorResponses);
                if (resolution.Error != null)
                {
                    var err = resolution.Error;
                    if (string.IsNullOrEmpty(err.Id))
                        err.Id = request?.Id ?? string.Empty;
                    return err;
                }
                effective = new TrameRequest()
                {
                    Id = request!.Id,
                    Controller = request.Controller,
                    Method = request.Method,
                    StringData = resolution.ResolvedJson,
                    DependencyMapping = request.DependencyMapping,
                };
            }

            var response = await ExecuteSingleInvocation(effective, context, ct);
            if (response != null && string.IsNullOrEmpty(response.Id))
                response.Id = effective?.Id ?? string.Empty;
            return response;
        }


        /// <summary>
        /// Löst Alias-Platzhalter (@alias) in den Parametern eines Requests anhand der
        /// ExposedDependencies vorheriger Responses auf. Gibt BadRequest zurück, wenn
        /// ein Alias nicht auflösbar ist.
        /// </summary>
        private (string? ResolvedJson, TrameResponse? Error) ResolveParameterValues(
            TrameRequest current,
            ConcurrentDictionary<string, TrameResponse?>? previousResponses)
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
            catch (Exception)
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
                return (null, BadRequest($"Unresolved dependencies: {string.Join(", ", unresolved)}"));
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
            if (node == null) return false;
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
                        // actualValue ist die JSON-Form des extrahierten Werts (siehe
                        // exposed[alias] = extracted.ToJsonString()) — z. B. "2" (int),
                        // "\"hi\"" (string) oder "{...}" (Objekt). Das `data`-Feld von
                        // TrameParameter ist selbst ein string, der den JSON-serialisierten
                        // Wert aufnimmt. Wir hüllen actualValue daher als JSON-String-Knoten
                        // ein (JsonValue.Create), damit es beim erneuten Serialisieren als
                        // String-Feld herauskommt und BuildParameters per
                        // JsonSerializer.Deserialize<T>(data) den typgetreuen Wert
                        // rekonstruiert. JsonNode.Parse würde stattdessen einen Number-/
                        // Object-Knoten erzeugen — dann ist `data` kein string mehr und die
                        // TrameParameter-Deserialisierung schlägt fehl.
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


        private async Task<TrameResponse?> ExecuteSingleInvocation(
            TrameRequest request,
            HttpContext? context,
            CancellationToken ct)
        {
            try
            {
                var controllerType = GetControllerType(request.Controller);
                if (controllerType == null)
                    return BadRequest($"Controller '{request.Controller}' not found.", HttpStatusCode.NotFound);

                string key = $"{request.Controller}_{request.Method}";
                if (!_invokeCache.TryGetValue(key, out var invokeInfo))
                    return BadRequest($"Method '{request.Method}' not found on controller '{request.Controller}'.");

                try
                {
                    await CheckAuthorisation(invokeInfo.AuthoriseAttribute, context);
                }
                catch (UnauthorizedAccessException)
                {
                    return Unauthorized();
                }

                var parameters = BuildParameters(request.StringData, invokeInfo.MethodInfo!.GetParameters(), ct);
                // BuildParameters liefert bei Fehler (Cap überschritten, Duplikat-Name,
                // falscher Typ, ungültiges JSON) (Items: null, Response: BadRequest). Der
                // Vergleich `== default` ((null,null)) fängt (null, BadRequest) NICHT ab —
                // dann fiel der Fehler durch zu ExecuteMethod(null-Args) → NRE → 500, und
                // die saubere 400 (z. B. „überschreitet MaxParameterArrayLength") ging
                // verloren. Auf Items==null prüfen (wie der Single-Call-Pfad bei Zeile 218).
                if (parameters.Items == null) return parameters.Response;

                var result = await ExecuteMethod(invokeInfo, controllerType, parameters.Items!, ct);
                if (result != null)
                    result.Id = request.Id;

                if (request.DependencyMapping != null && result?.Data.HasValue == true)
                {
                    var exposed = new Dictionary<string, string>();
                    // DependencyResolver extrahiert Werte aus dem strukturierten Data (JsonElement).
                    // byte[]-Returns liegen nicht mehr in Data (sondern in Content) → HasValue ist
                    // dort false und der Block wird sauber übersprungen (kein 500 durch unparsebares
                    // Base64 mehr — der alte Sonderfall entfällt mit dem Single-Pass-Modell).
                    foreach (var kv in request.DependencyMapping)
                    {
                        string alias = kv.Key;
                        string jsonPath = kv.Value;
                        try
                        {
                            var extracted = DependencyResolver.ExtractValue(result.Data.Value, jsonPath);
                            if (extracted != null)
                                // ToJsonString (nicht ToString!) bewahrt den JSON-Typ: Zahl/Bool/Object
                                // bleiben korrekt kodiert, damit die @alias-Substitution weiter unten
                                // typgetreu injiziert wird (siehe ReplaceDependencyByAlias).
                                exposed[alias] = extracted.ToJsonString();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Dependency-Extraktion für Alias {Alias} (Pfad {Path}) fehlgeschlagen — Data lässt sich nicht auflösen.",
                                alias, jsonPath);
                        }
                    }
                    result.ExposedDependencies = exposed;
                }

                return result;
            }
            catch (Exception ex)
            {
                return InternalServerError("An internal error occurred while processing the request.", ex);
            }
        }

        #endregion

        #region Parameter-Bau und Validierung

        private (object?[]? Items, TrameResponse? Response) BuildParameters(
            string? jsonInput,
            ParameterInfo[] methodParams,
            CancellationToken ct)
        {
            try
            {
                List<TrameParameter>? requestParams;
                if (!string.IsNullOrEmpty(jsonInput))
                {
                    requestParams = JsonSerializer.Deserialize<List<TrameParameter>>(jsonInput, _jsonSerializerOptions);
                    if (requestParams == null || !requestParams.All(rp => rp.Data != null))
                        throw new FormatException("Invalid parameter structure.");
                }
                else
                {
                    requestParams = new List<TrameParameter>();
                }

                // Nach Parameternamen indizieren. Doppelte Namen als BadRequest melden,
                // statt über ToDictionary eine unbehandelte Exception auszulösen.
                var byName = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var rp in requestParams)
                {
                    if (string.IsNullOrEmpty(rp.ParameterName))
                        continue;
                    if (byName.ContainsKey(rp.ParameterName))
                        return (null, BadRequest($"Duplicate parameter name '{rp.ParameterName}'."));
                    byName[rp.ParameterName] = rp.Data;
                }

                // Parameter ohne CancellationToken in der Reihenfolge der Methodensignatur.
                // Der clientseitige TrameCall-Builder vergibt Num ohnehin ohne Kenntnis der
                // CancellationToken-Parameter, daher wird hier an der bereinigten Folge gebunden.
                var nonTokenParams = methodParams
                    .Where(p => p.ParameterType != typeof(CancellationToken))
                    .ToArray();

                var parameters = new object?[methodParams.Length];
                foreach (var param in methodParams)
                {
                    if (param.ParameterType == typeof(CancellationToken))
                    {
                        parameters[param.Position] = ct;
                        continue;
                    }

                    string name = param.Name ?? $"param{param.Position}";

                    // 1. Bindung nach Parameternamen (bevorzugt).
                    bool matched = byName.TryGetValue(name, out var jsonValue);

                    // 2. Positions-Fallback: über Num, falls kein Name gepasst hat.
                    //    byte[]-Parameter werden hier ausgespart, da sie nachträglich über
                    //    InjectBinaryParameters aus TrameRequest.BinaryData gefüllt werden.
                    if (!matched && param.ParameterType != typeof(byte[]))
                    {
                        int positionalIndex = Array.IndexOf(nonTokenParams, param);
                        var positional = requestParams.FirstOrDefault(rp => rp.Num == positionalIndex);
                        if (positional != null)
                        {
                            jsonValue = positional.Data;
                            matched = true;
                        }
                    }

                    if (matched)
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
                        catch (Exception)
                        {
                            return (null, BadRequest($"Parameter '{name}' cannot be converted to type '{param.ParameterType.Name}'."));
                        }

                        // Kardinalitäts-Cap auf Collection-Parametern (Top-Level). Schützt
                        // vor Riesen-Arrays, die insb. beim @alias-Whole-Collection-Passthrough
                        // serverseitig erzeugt werden (Body-Size-Limits greifen dort nicht).
                        // string ausnehmen (ist IEnumerable<char>); byte[] wird via
                        // InjectBinaryParameters gefüllt und ist keine Kardinalitäts-Frage.
                        if (MaxParameterArrayLength > 0
                            && parameters[param.Position] is not string and not byte[]
                            && parameters[param.Position] is ICollection colParam
                            && colParam.Count > MaxParameterArrayLength)
                        {
                            return (null, BadRequest(
                                $"Parameter '{name}' überschreitet MaxParameterArrayLength " +
                                $"({MaxParameterArrayLength}; Ist {colParam.Count}). " +
                                "Paginieren oder Cap erhöhen (0 = unbegrenzt)."));
                        }
                    }
                    else
                    {
                        // Falls der Parameter fehlt, den Default-Wert verwenden
                        parameters[param.Position] = GetDefault(param.ParameterType);
                    }
                }
                return (parameters, null);
            }
            catch (Exception)
            {
                return (null, BadRequest("Error processing request parameters."));
            }
        }


        /// <summary>
        /// Injects binary data from TrameRequest.BinaryData into byte[] parameters.
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

        private async Task<TrameResponse?> ExecuteMethod(
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
                    return InternalServerError($"Failed to resolve controller '{controllerType.Name}' from the DI container.");

                try
                {
                    var compiled = invokeInfo.CompiledInvocation;
                    if (compiled == null)
                    {
                        return InternalServerError($"Method '{invokeInfo.MethodInfo?.Name}' is not compiled.");
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
                        object list;
                        try
                        {
                            list = await ConsumeAsyncEnumerable(result, elementType, ct, MaxResultElementCount);
                        }
                        catch (ResultCardinalityExceededException ex)
                        {
                            return BadRequest(ex.Message, HttpStatusCode.RequestEntityTooLarge);
                        }
                        return Ok(JsonSerializer.SerializeToUtf8Bytes(list, _jsonSerializerOptions));
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
                                object list;
                                try
                                {
                                    list = await ConsumeAsyncEnumerable(resultValue, elementType, ct, MaxResultElementCount);
                                }
                                catch (ResultCardinalityExceededException ex)
                                {
                                    return BadRequest(ex.Message, HttpStatusCode.RequestEntityTooLarge);
                                }
                                return Ok(JsonSerializer.SerializeToUtf8Bytes(list, _jsonSerializerOptions));
                            }

                            return ReturnResponse(resultValue, instance?.GetType());
                        }

                        // Task ohne Ergebnis (async void-ähnlich) → 204 No Content.
                        return NoContent();
                    }

                    // Synchrone void-Methoden → 204 No Content.
                    if (!invokeInfo.HasResult)
                        return NoContent();

                    return ReturnResponse(result, controllerType);
                }
                catch (TargetInvocationException tie)
                {
                    return InternalServerError("An internal error occurred while processing the request.", tie.InnerException);
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
        private static async Task<object> ConsumeAsyncEnumerable(
            object asyncEnumerable, Type elementType, CancellationToken ct, int maxElements)
        {
            var helperType = typeof(AsyncEnumerableConsumer<>).MakeGenericType(elementType);
            var method = helperType.GetMethod("Consume")!;
            var task = (Task)method.Invoke(null, new[] { asyncEnumerable, ct, maxElements })!;
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }

        /// <summary>
        /// Signalisiert, dass ein IAsyncEnumerable-Resultat die MaxResultElementCount
        /// überschritten hat. Wird im Streaming-Pfad geworfen, damit der Konsum früh
        /// abgebrochen wird (bevor eine Riesen-Liste allokiert ist) statt erst nach
        /// vollständiger Materialisierung.
        /// </summary>
        private sealed class ResultCardinalityExceededException : Exception
        {
            public ResultCardinalityExceededException(int cap, int count)
                : base($"Stream überschreitet MaxResultElementCount ({cap}; bei Element {count}). Paginieren oder Cap erhöhen (0 = unbegrenzt).")
            { }
        }

        /// <summary>
        /// Generic helper class for consuming IAsyncEnumerable<T> without reflection.
        /// </summary>
        private static class AsyncEnumerableConsumer<T>
        {
            public static async Task<List<T>> Consume(IAsyncEnumerable<T> source, CancellationToken ct, int maxElements)
            {
                var list = new List<T>();
                await foreach (var item in source.WithCancellation(ct))
                {
                    // Early-Stop: nicht erst die komplette Riesen-Sequenz allokieren.
                    if (maxElements > 0 && list.Count >= maxElements)
                        throw new ResultCardinalityExceededException(maxElements, list.Count);
                    list.Add(item);
                }
                return list;
            }
        }
        #endregion

        #region Hilfsmethoden & Response-Fabriken

        private static T? GetAttribute<T>(MemberInfo member) where T : Attribute
            => (T?)member.GetCustomAttributes(typeof(T), false).FirstOrDefault();

        private TrameResponse ReturnResponse(object? result, Type? instanceType)
        {
            // null-Ergebnis → 200 mit leerem Data (kein 204 — die Methode hat erfolgreich
            // kein Ergebnis geliefert, das ist ein anderer Zustand als void/Task).
            if (result is null) return Ok((byte[]?)null);

            if (result is TrameResponse trameResp)
                return trameResp;

            // Binary return: Bytes landen ausschließlich in Content (kein Base64-String
            // mehr in Data → keine doppelte Belegung, kein Parse-Problem im Dep-Pfad).
            if (result is byte[] bytes)
                return new TrameResponse { Code = (int)HttpStatusCode.OK, Content = bytes };

            // Kardinalitäts-Cap auf Top-Level-Collection-Resultaten (List/Array/Dictionary).
            // string ist IEnumerable<char> → ausgenommen. Streaming-Returns werden vorher
            // konsumiert und in ConsumeAsyncEnumerable frühzeitig gestoppt (siehe unten),
            // landen aber als materialisierte List<T> auch hier — dieser Check ist der
            // einheitliche Rückfall für Task<T>/Sync-Returns.
            if (MaxResultElementCount > 0
                && result is not string
                && result is ICollection colResult
                && colResult.Count > MaxResultElementCount)
            {
                return BadRequest(
                    $"Rückgabewert überschreitet MaxResultElementCount " +
                    $"({MaxResultElementCount}; Ist {colResult.Count}). " +
                    "Paginieren oder Cap erhöhen (0 = unbegrenzt).",
                    HttpStatusCode.RequestEntityTooLarge);
            }

            // Single-Pass: object → rohe UTF-8-Bytes (kein JsonDocument-Baum). Der
            // Transport-Converter schreibt DataBytes via WriteRawValue direkt in den
            // Wire. Data bleibt null und wird nur bei Dep-Chaining lazy gelesen.
            try { return Ok(JsonSerializer.SerializeToUtf8Bytes(result, _jsonSerializerOptions)); }
            catch
            {
                _logger.LogWarning("Failed to serialize result of type {ResultType} from controller {ControllerType}",
                    result.GetType().Name, instanceType?.FullName);
                return InternalServerError("Failed to serialize the response.");
            }
        }

        private Type? GetControllerType(string controllerName)
            => _routeHandlers.GetValueOrDefault(controllerName);

        private async Task CheckAuthorisation(TrameAuthoriseAttribute? authAttr, HttpContext? context)
        {
            if (authAttr != null && !await authAttr.OnAuthorization(context))
                throw new UnauthorizedAccessException();
        }

        #endregion

        #region Response-Generierung

        private TrameResponse BadRequest(string message, HttpStatusCode code = HttpStatusCode.BadRequest)
            => CreateError(code, message);

        // Erfolg: strukturierter Ergebniswert in Data (roh, ein Pass — kein Double-Wrapping).
        // Der Bulk-Pfad nutzt Ok(byte[]) mit rohen UTF-8-Bytes (DataBytes); Data bleibt
        // null und wird erst lazy materialisiert, wenn ein Reader (Dep-Chaining, Tests)
        // darauf zugreift. Ok(JsonElement?) bleibt für den Legacy/Spezial-Pfad.
        private TrameResponse Ok(JsonElement? data)
            => new() { Code = (int)HttpStatusCode.OK, Data = data };

        private TrameResponse Ok(byte[]? dataBytes)
            => new() { Code = (int)HttpStatusCode.OK, DataBytes = dataBytes };

        private TrameResponse NoContent()
            => new() { Code = (int)HttpStatusCode.NoContent };

        private TrameResponse Unauthorized()
            => CreateError(HttpStatusCode.Unauthorized, "Unauthorized.");

        private TrameResponse InternalServerError(string message)
            => CreateError(HttpStatusCode.InternalServerError, message);

        /// <summary>
        /// Erzeugt eine 500-Response. Die Message bleibt generisch (kein Leak in Produktion);
        /// die echten Exception-Details werden nur bei aktiviertem EnableDetailedErrors
        /// in TrameError.Details abgelegt.
        /// </summary>
        private TrameResponse InternalServerError(string message, Exception? ex)
        {
            var response = CreateError(HttpStatusCode.InternalServerError, message);
            if (ex != null && EnableDetailedErrors && response.Error != null)
                response.Error.Details = ex.ToString();
            return response;
        }

        /// <summary>
        /// Fehler-Response: Data bleibt null (Fehler tragen nur Code + Error-Objekt),
        /// die Message wohnt in Error.Message. Kein String-Payload in Data mehr.
        /// </summary>
        private TrameResponse CreateError(HttpStatusCode code, string? message)
        {
            return new TrameResponse
            {
                Code = (int)code,
                Data = null,
                Error = new TrameError
                {
                    Code = (int)code,
                    Message = message ?? "Unknown error"
                }
            };
        }

        #endregion

        public class InvokeInfo
        {
            public MethodInfo MethodInfo { get; set; } = null!;
            public TrameAuthoriseAttribute? AuthoriseAttribute { get; set; }

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
