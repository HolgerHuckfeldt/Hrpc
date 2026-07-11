// ==============================================================================
// 04 — Dependency Chaining (C#-Client)
// ==============================================================================
// Mehrere Aufrufe in EINER Roundtrip, wobei spätere Aufrufe Werte aus früheren
// nutzen — ohne Client-seitiges Zusammenfügen.
//
// Mechanik:
//   • Request A deklariert DependencyMapping: { "alias" → "$.JsonPath" }.
//     Der JsonPath ist ergebnisrelativ: "$" = der gesamte serialisierte
//     Rückgabewert; "$.Id" = Eigenschaft; "$[0].Id" = erstes Listenelement.
//     KEIN "$.data"-Envelope.
//   • Der Server extrahiert den Wert und speichert ihn in ExposedDependencies.
//   • Request B nutzt "@alias" als Parameterwert (Data-String mit @-Präfix).
//     Der Server ersetzt es vor der Ausführung.
//   • Mode = Serial (sobald ein DependencyMapping existiert, schaltet der Server
//     ohnehin auf topologische Batch-Ausführung — Mode wird dann ignoriert).
// ==============================================================================

using WaevaRpcClient.WaevaRpc;
using WaevaRpcCommon.Models;
using System.Text.Json;

using var client = new WaevaRpcRestJsonClient("https://localhost:5001");

// -----------------------------------------------------------------------------
// Variante A — Fluent Builder (einfach, 2-Step, Ein-Parameter-@alias)
// -----------------------------------------------------------------------------
// AddCustomer → liefert neue Id (int) → weiter als @newId an GetCustomerById.
var multi = new WaevaRpcMultiRequest
{
    Mode = ExecutionMode.Serial,
    Requests = new List<WaevaRpcRequest>
    {
        WaevaRpcCall.Init("Customer", "AddCustomer")
            .Named("step1")
            .Param("name", "Alice")
            .Param("email", "alice@x.com")
            .Exposes("$", "newId")          // ganzer int-Rückgabewert → Alias "newId"
            .ToRequest(),

        WaevaRpcCall.Init("Customer", "GetCustomerById")
            .Named("step2")
            .WithAlias("@newId")            // Data="@newId"; ParameterName="newId"
            .ToRequest(),
        // Hinweis: WithAlias setzt ParameterName auf den Alias-Namen ("newId").
        // Da GetCustomerById nur EINEN echten Parameter ("id") hat, bindet der
        // Server positional (Num=0) → klappt. Bei MEHRPARAMETRIGEN Methoden mit
        // @alias siehe Variante B (raw, ParameterName = echter Parametername).
    },
};

var responses = (await client.Call(multi))!.ToList();
// responses[0].Data = neue Id (int);  responses[1].Data = der angelegte Customer.

// -----------------------------------------------------------------------------
// Variante B — Raw-Form (robust, 3-Step, mehrparametrige @alias-Bindung)
// -----------------------------------------------------------------------------
// AddCustomer → @custId → CreateOrder(customerId=@custId, total=99.90) → @orderId
// → GetOrder(@orderId). CreateOrder hat ZWEI Parameter, davon einer @alias —
// deshalb setzen wir ParameterName auf den echten Parameternamen ("customerId"),
// damit der Server nach Name bindet (sicherer als positional).
var chain = new WaevaRpcMultiRequest
{
    Mode = ExecutionMode.Serial,
    Requests = new List<WaevaRpcRequest>
    {
        // step1: Kunden anlegen, neue Id als "custId" weitergeben.
        new WaevaRpcRequest
        {
            Controller = "Customer", Method = "AddCustomer", Id = "step1",
            StringData = JsonSerializer.Serialize(new[]
            {
                new WaevaRpcParameter { Num = 0, ParameterName = "name",  Data = JsonSerializer.Serialize("Alice") },
                new WaevaRpcParameter { Num = 1, ParameterName = "email", Data = JsonSerializer.Serialize("alice@x.com") },
            }),
            DependencyMapping = new Dictionary<string, string> { ["custId"] = "$" },
        },

        // step2: Bestellung für diesen Kunden anlegen; customerId kommt von @custId,
        // total ist ein Literal. OrderId als "orderId" weitergeben.
        new WaevaRpcRequest
        {
            Controller = "Order", Method = "CreateOrder", Id = "step2",
            StringData = JsonSerializer.Serialize(new[]
            {
                // Data="@custId" → Server erkennt @-Präfix und substituiert.
                new WaevaRpcParameter { Num = 0, ParameterName = "customerId", Data = "@custId" },
                new WaevaRpcParameter { Num = 1, ParameterName = "total",      Data = JsonSerializer.Serialize(99.90m) },
            }),
            DependencyMapping = new Dictionary<string, string> { ["orderId"] = "$" },
        },

        // step3: Bestellung anhand der weitergegebenen OrderId laden.
        new WaevaRpcRequest
        {
            Controller = "Order", Method = "GetOrderById", Id = "step3",
            StringData = JsonSerializer.Serialize(new[]
            {
                new WaevaRpcParameter { Num = 0, ParameterName = "id", Data = "@orderId" },
            }),
        },
    },
};

var chainResponses = (await client.Call(chain))!.ToList();
// chainResponses[0].Data = neue CustomerId (int)
// chainResponses[1].Data = neue OrderId (int)
// chainResponses[2].Data = die angelegte Order (mit CustomerId, Total, CreatedAt)

// -----------------------------------------------------------------------------
// Gotchas
// -----------------------------------------------------------------------------
// • JsonPath ist ergebnisrelativ (kein $.data-Envelope): "$", "$.Id", "$[0].Id".
// • Ein UNAUFGELÖSTES @alias → Server antwortet 400 "Unresolved dependencies".
//   Stellen sicher, dass jeder @alias VOR seiner Nutzung deklariert ist
//   (DependencyMapping eines früheren Requests in derselben Batch).
// • Zirkuläre Abhängigkeiten → 400 für ALLE Requests der Batch.
// • Binäre Rückgabewerte (byte[]) landen in .Content, nicht in .Data → für sie
//   kann kein JsonPath/​@alias extrahiert werden.