// ==============================================================================
// WaevaRpc Beispiel-Server (Drop-in) — unterstützt alle Szenarien aus samples/.
//
// Zeigt: Controller-Deklaration via [WaevaRpcController]/[WaevaRpcMethod], DTOs als
// [WaevaRpcDataContract], Fehlerbehandlung via WaevaRpcResults, und Rückgabewerte, die
// für Dependency-Chaining (@alias) geeignet sind.
//
// In-Memory-Store NUR für Demos — in Produktion DI-Repositories verwenden.
// ==============================================================================

using WaevaRpcCommon.Attribute;   // WaevaRpcDataContract, WaevaRpcDocumentation, WaevaRpcExample
using WaevaRpcCommon.Results;     // WaevaRpcResults (Ok/NotFound/BadRequest)
using WaevaRpcCore.Attributes;    // WaevaRpcController, WaevaRpcMethod

namespace WaevaRpc.Samples.Server;

// --- DTOs (der Vertrag) -------------------------------------------------------

[WaevaRpcDataContract]
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

[WaevaRpcDataContract]
public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
}

// --- In-Memory-Store (Demo) ---------------------------------------------------

// Static-Store nur für das Beispiel. In einer echten App via DI als Singleton/
// Scoped registrieren und in den Controller injizieren.
public static class DemoStore
{
    private static int _nextCustomerId = 1;
    private static int _nextOrderId = 1;
    private static readonly List<Customer> _customers = new();
    private static readonly List<Order> _orders = new();

    public static Task<int> AddCustomerAsync(string name, string email, CancellationToken ct)
    {
        var c = new Customer { Id = Interlocked.Increment(ref _nextCustomerId), Name = name, Email = email };
        _customers.Add(c);
        return Task.FromResult(c.Id); // Rückgabe = neue Id (ganzer int → JsonPath "$")
    }

    public static Task<Customer?> GetCustomerByIdAsync(int id, CancellationToken ct)
        => Task.FromResult(_customers.FirstOrDefault(c => c.Id == id));

    public static Task<IReadOnlyList<Customer>> GetAllCustomersAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Customer>>(_customers);

    public static Task<int> CreateOrderAsync(int customerId, decimal total, CancellationToken ct)
    {
        // Business-Validierung → Domänenfehler als WaevaRpcResponse (kein Throw).
        if (!_customers.Any(c => c.Id == customerId))
            return Task.FromResult(-1); // siehe Controller: dort → WaevaRpcResults.BadRequest

        var o = new Order
        {
            Id = Interlocked.Increment(ref _nextOrderId),
            CustomerId = customerId,
            Total = total,
            CreatedAt = DateTime.UtcNow
        };
        _orders.Add(o);
        return Task.FromResult(o.Id); // Rückgabe = neue OrderId (→ JsonPath "$")
    }

    public static Task<Order?> GetOrderByIdAsync(int id, CancellationToken ct)
        => Task.FromResult(_orders.FirstOrDefault(o => o.Id == id));
}

// --- Controller ---------------------------------------------------------------

[WaevaRpcController("Customer")]
[WaevaRpcDocumentation("Kunden verwalten — AddCustomer liefert die neue Id (int).")]
public class CustomerController
{
    [WaevaRpcMethod("AddCustomer")]
    [WaevaRpcDocumentation("Legt einen Kunden an. Rückgabe: neue Id (skalarer int → JsonPath '$').")]
    [WaevaRpcExample("{\"name\":\"Alice\",\"email\":\"alice@x.com\"}")]
    public Task<int> AddCustomer(string name, string email, CancellationToken ct)
        => DemoStore.AddCustomerAsync(name, email, ct);

    [WaevaRpcMethod("GetCustomerById")]
    [WaevaRpcDocumentation("Lädt einen Kunden nach Id. Rückgabe: Customer oder 404.")]
    public async Task<WaevaRpcResponse> GetCustomerById(int id, CancellationToken ct)
    {
        var c = await DemoStore.GetCustomerByIdAsync(id, ct);
        return c is null
            ? WaevaRpcResults.NotFound($"Kunde {id} nicht gefunden")
            : WaevaRpcResults.Ok(c);
    }

    [WaevaRpcMethod("GetAllCustomers")]
    [WaevaRpcDocumentation("Alle Kunden. Rückgabe: Liste → JsonPath '$[0].Id' für das erste Element.")]
    public Task<IReadOnlyList<Customer>> GetAllCustomers(CancellationToken ct)
        => DemoStore.GetAllCustomersAsync(ct);
}

[WaevaRpcController("Order")]
[WaevaRpcDocumentation("Bestellungen verwalten — CreateOrder liefert die neue OrderId (int).")]
public class OrderController
{
    [WaevaRpcMethod("CreateOrder")]
    [WaevaRpcDocumentation("Legt eine Bestellung an. Parameter: customerId, total. Rückgabe: neue OrderId.")]
    [WaevaRpcExample("{\"customerId\":1,\"total\":99.90}")]
    public async Task<WaevaRpcResponse> CreateOrder(int customerId, decimal total, CancellationToken ct)
    {
        var orderId = await DemoStore.CreateOrderAsync(customerId, total, ct);
        // Domänenfehler → WaevaRpcResponse (Code+Message erreichen den Client unverfälscht).
        return orderId < 0
            ? WaevaRpcResults.BadRequest($"Kunde {customerId} existiert nicht")
            : WaevaRpcResults.Ok(orderId); // skalarer int → JsonPath "$"
    }

    [WaevaRpcMethod("GetOrderById")]
    [WaevaRpcDocumentation("Lädt eine Bestellung nach Id. Rückgabe: Order oder 404.")]
    public async Task<WaevaRpcResponse> GetOrderById(int id, CancellationToken ct)
    {
        var o = await DemoStore.GetOrderByIdAsync(id, ct);
        return o is null
            ? WaevaRpcResults.NotFound($"Bestellung {id} nicht gefunden")
            : WaevaRpcResults.Ok(o);
    }
}

// --- Server-Setup (Program.cs) — kurze Form -----------------------------------
//
// using WaevaRpcHub.Extensions;
// using WaevaRpcServer;                       // UseWaevaRpcTransports, MapWaevaRpc
//
// var builder = WebApplication.CreateBuilder(args);
// builder.Services.AddWaevaRpc(o =>
// {
//     o.EnableDetailedErrors = builder.Environment.IsDevelopment();
//     o.RateLimitPermitLimit = builder.Environment.IsProduction() ? 50 : 0;
// });
//
// var app = builder.Build();
// app.UseRouting();
// app.UseRateLimiter();
// app.UseWaevaRpcTransports();   // WebSocket (primär) + Controller-Registrierung
// app.MapWaevaRpc();             // REST (/api/waeva) + DevUI (/developer) + ggf. SignalR
// app.Run();
//
// Die [WaevaRpcController]-Typen werden per Attribut-Scan automatisch gefunden und
// beim Aufruf von UseWaevaRpc() im Invoker registriert — keine manuelle Registrierung.