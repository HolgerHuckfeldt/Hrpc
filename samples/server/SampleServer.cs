// ==============================================================================
// Hrpc Beispiel-Server (Drop-in) — unterstützt alle Szenarien aus samples/.
//
// Zeigt: Controller-Deklaration via [HrpcController]/[HrpcMethod], DTOs als
// [HrpcDataContract], Fehlerbehandlung via HrpcResults, und Rückgabewerte, die
// für Dependency-Chaining (@alias) geeignet sind.
//
// In-Memory-Store NUR für Demos — in Produktion DI-Repositories verwenden.
// ==============================================================================

using HrpcCommon.Attribute;   // HrpcDataContract, HrpcDocumentation, HrpcExample
using HrpcCommon.Results;     // HrpcResults (Ok/NotFound/BadRequest)
using HrpcCore.Attributes;    // HrpcController, HrpcMethod

namespace Hrpc.Samples.Server;

// --- DTOs (der Vertrag) -------------------------------------------------------

[HrpcDataContract]
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

[HrpcDataContract]
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
        // Business-Validierung → Domänenfehler als HrpcResponse (kein Throw).
        if (!_customers.Any(c => c.Id == customerId))
            return Task.FromResult(-1); // siehe Controller: dort → HrpcResults.BadRequest

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

[HrpcController("Customer")]
[HrpcDocumentation("Kunden verwalten — AddCustomer liefert die neue Id (int).")]
public class CustomerController
{
    [HrpcMethod("AddCustomer")]
    [HrpcDocumentation("Legt einen Kunden an. Rückgabe: neue Id (skalarer int → JsonPath '$').")]
    [HrpcExample("{\"name\":\"Alice\",\"email\":\"alice@x.com\"}")]
    public Task<int> AddCustomer(string name, string email, CancellationToken ct)
        => DemoStore.AddCustomerAsync(name, email, ct);

    [HrpcMethod("GetCustomerById")]
    [HrpcDocumentation("Lädt einen Kunden nach Id. Rückgabe: Customer oder 404.")]
    public async Task<HrpcResponse> GetCustomerById(int id, CancellationToken ct)
    {
        var c = await DemoStore.GetCustomerByIdAsync(id, ct);
        return c is null
            ? HrpcResults.NotFound($"Kunde {id} nicht gefunden")
            : HrpcResults.Ok(c);
    }

    [HrpcMethod("GetAllCustomers")]
    [HrpcDocumentation("Alle Kunden. Rückgabe: Liste → JsonPath '$[0].Id' für das erste Element.")]
    public Task<IReadOnlyList<Customer>> GetAllCustomers(CancellationToken ct)
        => DemoStore.GetAllCustomersAsync(ct);
}

[HrpcController("Order")]
[HrpcDocumentation("Bestellungen verwalten — CreateOrder liefert die neue OrderId (int).")]
public class OrderController
{
    [HrpcMethod("CreateOrder")]
    [HrpcDocumentation("Legt eine Bestellung an. Parameter: customerId, total. Rückgabe: neue OrderId.")]
    [HrpcExample("{\"customerId\":1,\"total\":99.90}")]
    public async Task<HrpcResponse> CreateOrder(int customerId, decimal total, CancellationToken ct)
    {
        var orderId = await DemoStore.CreateOrderAsync(customerId, total, ct);
        // Domänenfehler → HrpcResponse (Code+Message erreichen den Client unverfälscht).
        return orderId < 0
            ? HrpcResults.BadRequest($"Kunde {customerId} existiert nicht")
            : HrpcResults.Ok(orderId); // skalarer int → JsonPath "$"
    }

    [HrpcMethod("GetOrderById")]
    [HrpcDocumentation("Lädt eine Bestellung nach Id. Rückgabe: Order oder 404.")]
    public async Task<HrpcResponse> GetOrderById(int id, CancellationToken ct)
    {
        var o = await DemoStore.GetOrderByIdAsync(id, ct);
        return o is null
            ? HrpcResults.NotFound($"Bestellung {id} nicht gefunden")
            : HrpcResults.Ok(o);
    }
}

// --- Server-Setup (Program.cs) — kurze Form -----------------------------------
//
// using HrpcHub.Extensions;
// using HrpcServer;                       // UseHrpcTransports, MapHrpc
//
// var builder = WebApplication.CreateBuilder(args);
// builder.Services.AddHrpc(o =>
// {
//     o.EnableDetailedErrors = builder.Environment.IsDevelopment();
//     o.RateLimitPermitLimit = builder.Environment.IsProduction() ? 50 : 0;
// });
//
// var app = builder.Build();
// app.UseRouting();
// app.UseRateLimiter();
// app.UseHrpcTransports();   // WebSocket (primär) + Controller-Registrierung
// app.MapHrpc();             // REST (/api/hrpc) + DevUI (/developer) + ggf. SignalR
// app.Run();
//
// Die [HrpcController]-Typen werden per Attribut-Scan automatisch gefunden und
// beim Aufruf von UseHrpc() im Invoker registriert — keine manuelle Registrierung.