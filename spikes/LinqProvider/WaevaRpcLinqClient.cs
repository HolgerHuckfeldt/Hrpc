using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using WaevaRpcClient.WaevaRpc;
using WaevaRpcCommon.Models;

namespace WaevaRpc.Spike.LinqProvider;

/// <summary>
/// LINQ-Provider-artiger Client: baut aus einem typsicheren Lambda
/// <c>(TService c) =&gt; c.Method(args)</c> einen <see cref="WaevaRpcCallSpec{T}"/>.
/// Controller-/Methoden-Namen kommen aus den Vertrags-Attriben
/// (<see cref="WaevaRpcServiceContractAttribute"/> / <see cref="WaevaRpcMethodContractAttribute"/>),
/// Parameternamen aus der Methoden-Signatur. Argumente werden JSON-serialisiert;
/// <see cref="Dep{T}"/>-Argumente werden zu <c>@alias</c>-Platzhaltern.
/// </summary>
public sealed class WaevaRpcLinqClient
{
    private readonly IWaevaRpcClient _transport;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private int _idCounter;

    public WaevaRpcLinqClient(IWaevaRpcClient transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>
    /// Baut einen typisierten Call mit Rückgabewert:
    /// <c>client.Build((ICustomerService c) =&gt; c.AddCustomer("Alice"))</c> →
    /// <see cref="WaevaRpcCallSpec{T}"/> mit T = int.
    /// </summary>
    public WaevaRpcCallSpec<T> Build<TService, T>(Expression<Func<TService, Task<T>>> call)
        where TService : class
        => (WaevaRpcCallSpec<T>)BuildCore(typeof(T), call);

    /// <summary>
    /// Baut einen void-Call (Methode ohne Rückgabewert):
    /// <c>client.BuildVoid((ICustomerService c) =&gt; c.DeleteCustomer(5))</c>.
    /// </summary>
    public WaevaRpcCallSpec<object> BuildVoid<TService>(Expression<Func<TService, Task>> call)
        where TService : class
        => (WaevaRpcCallSpec<object>)BuildCore(typeof(object), call);

    private WaevaRpcCallSpec BuildCore(Type resultType, LambdaExpression call)
    {
        if (call?.Body is not MethodCallExpression mce)
            throw new ArgumentException(
                "Erwartet ein einzelner Methodenaufruf auf dem Service, z. B. c => c.Method(args).");

        var svcType = mce.Method.DeclaringType
            ?? throw new ArgumentException("Methodenaufruf ohne deklarierenden Typ.");
        var controller = svcType.GetCustomAttribute<WaevaRpcServiceContractAttribute>()
            ?? throw new ArgumentException(
                $"{svcType.Name} trägt kein [WaevaRpcServiceContract] — ist es ein generierter Vertrag?");
        var methodAttr = mce.Method.GetCustomAttribute<WaevaRpcMethodContractAttribute>()
            ?? throw new ArgumentException(
                $"{mce.Method.Name} trägt kein [WaevaRpcMethodContract].");

        // Parameter-Namen aus der Methoden-Signatur; Argumente Werte-basiert auflösen.
        // Vertrags-Parameter sind Arg<T>-Wrapper — ein Arg trägt entweder einen
        // konkreten Wert oder einen Dep<T>-Platzhalter (→ @alias).
        var paramInfos = mce.Method.GetParameters();
        var parameters = new List<WaevaRpcParameter>(paramInfos.Length);
        for (int i = 0; i < paramInfos.Length; i++)
        {
            object? value = EvaluateArgument(mce.Arguments[i]);
            string data;
            if (value is IArg arg)
            {
                // Arg<T>: Dep → @alias (serverseitige Substitution), sonst Wert.
                data = arg.IsDep
                    ? "@" + arg.Alias
                    : JsonSerializer.Serialize(arg.Value, _jsonOpts);
            }
            else
            {
                // Fallback: nackter Wert (kein Arg<T>-Wrapper).
                data = JsonSerializer.Serialize(value, _jsonOpts);
            }
            parameters.Add(new WaevaRpcParameter
            {
                Num = i,
                ParameterName = paramInfos[i].Name ?? $"param{i}",
                Data = data
            });
        }

        var id = $"{controller.Controller}.{methodAttr.Method}#{++_idCounter}";
        var specType = typeof(WaevaRpcCallSpec<>).MakeGenericType(resultType);
        return (WaevaRpcCallSpec)Activator.CreateInstance(
            specType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new object[] { controller.Controller, methodAttr.Method, id, JsonSerializer.Serialize(parameters) },
            null)!;
    }

    /// <summary>
    /// Löst einen Argument-Expression zur Laufzeit auf. Der Spike kompiliert jedes
    /// Argument einzeln — damit sind Konstanten, erfasste Lokale (closures) und
    /// <see cref="Dep{T}"/>-Marker gleichermaßen abgedeckt. (In einem echten LINQ-
    /// Provider würde man konstante Teile zur Bauzeit einklappen.)
    /// </summary>
    private static object? EvaluateArgument(Expression arg)
    {
        // Konstante direkt — spart das Kompilieren und ist die häufigste Form.
        if (arg is ConstantExpression ce)
            return ce.Value;
        // Sonst kompilieren & ausführen (schließt erfasste Lokale/Deps ein).
        return Expression.Lambda(arg).Compile().DynamicInvoke();
    }

    /// <summary>
    /// Sendet einen einzelnen typisierten Call und deserialisiert das Resultat als T.
    /// </summary>
    public async Task<T?> SendAsync<T>(WaevaRpcCallSpec<T> spec, CancellationToken ct = default)
    {
        var resp = await _transport.Call(spec.ToRequest(), ct);
        return Deserialize<T>(resp);
    }

    /// <summary>
    /// Sendet einen Batch (Multi-Request) über den Transport. Liefert die rohen
    /// Responses; typisierte Extraktion via <see cref="ResultOf{T}"/>.
    /// </summary>
    public async Task<IReadOnlyList<WaevaRpcResponse>> SendAsync(
        WaevaRpcBatch batch, CancellationToken ct = default)
    {
        var multi = batch.ToMultiRequest();
        var responses = await _transport.Call(multi, ct);
        return responses?.Where(r => r != null).Cast<WaevaRpcResponse>().ToList()
               ?? new List<WaevaRpcResponse>();
    }

    /// <summary>
    /// Extrahiert das typisierte Resultat eines bestimmten Specs aus einer Batch-
    /// Response-Liste (Korrelation über die Id).
    /// </summary>
    public T? ResultOf<T>(WaevaRpcCallSpec<T> spec, IReadOnlyList<WaevaRpcResponse> responses)
    {
        var resp = responses.FirstOrDefault(r => r.Id == spec.Id);
        return Deserialize<T>(resp);
    }

    private static T? Deserialize<T>(WaevaRpcResponse? resp)
    {
        if (resp == null || resp.Code is < 200 or > 299) return default;
        if (string.IsNullOrEmpty(resp.Data)) return default;
        try { return JsonSerializer.Deserialize<T>(resp.Data!, _jsonOpts); }
        catch { return default; }
    }
}

/// <summary>
/// Wurzel-Typ eines gebauten Calls. Hält den Zustand (Controller, Methode, Id,
/// serialisierte Parameter, dependencyMapping). Die generische
/// <see cref="WaevaRpcCallSpec{T}"/> fügt lediglich die typsicheren <c>Expose</c>-Methoden
/// hinzu; dieser Basistyp ist nicht-generisch, damit <see cref="WaevaRpcBatch"/> und
/// <see cref="WaevaRpcLinqClient.BuildVoid{TService}"/> ohne Typparameter arbeiten können.
/// </summary>
public abstract class WaevaRpcCallSpec
{
    public string Controller { get; protected set; }
    public string Method { get; protected set; }
    public string Id { get; protected set; }
    public string StringData { get; set; }
    public Dictionary<string, string>? DependencyMapping { get; set; }
    private int _exposeCounter;

    protected WaevaRpcCallSpec(string controller, string method, string id, string stringData)
    {
        Controller = controller;
        Method = method;
        Id = id;
        StringData = stringData;
    }

    /// <summary>
    /// Stellt einen JsonPath-Pfad aus dem Resultat als Alias bereit und registriert
    /// das zugehörige <c>dependencyMapping</c> an diesem Call. Wird von den
    /// generischen <c>Expose</c>-Methoden in <see cref="WaevaRpcCallSpec{T}"/> gerufen.
    /// </summary>
    protected Dep<TProp> ExposePath<TProp>(string path)
    {
        // Alias muss für den serverseitigen DependencyGraphBuilder rein
        // alphanumerisch + '_' sein (ExtractAliases bricht an '.', '#' u. ä. ab).
        // Sonst würde die Kante nicht erkannt und der Folge-Call im falschen
        // Batch landen — der @alias-Platzhalter bliebe unaufgelöst.
        var safeId = new string(Id.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        var alias = $"{safeId}__dep{++_exposeCounter}";
        (DependencyMapping ??= new Dictionary<string, string>())[alias] = path;
        return new Dep<TProp>(alias);
    }

    public WaevaRpcRequest ToRequest() => new()
    {
        Controller = Controller,
        Method = Method,
        Id = Id,
        StringData = StringData,
        DependencyMapping = DependencyMapping
    };
}