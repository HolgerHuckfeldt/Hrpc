# Story 01 — The N+1 Screen

> **The client should declare *what depends on what*. The server should resolve it in one roundtrip.**

One order-detail screen. Six dependent reads across five services. The REST way is six
sequential roundtrips with the client as the workflow engine. The Trame way is one batch —
the client declares the dependency graph, the server executes it topologically with
intra-server parallelism, in a single roundtrip.

## Run it (F5 → DevUI)

1. Open **`Story01.sln`** in Visual Studio (or `dotnet build && dotnet run --project Story01.csproj`).
2. Press **F5**. The browser opens at **`http://localhost:5001/Trame`** — the Trame Developer UI.
3. The DevUI lists six controllers from `Domain.cs`: `Order`, `Customer`, `OrderLine`,
   `Article`, `Address`, `Stock`. Discovery shows the contract (code-first — the C# classes *are*
   the contract, no IDL, no `.proto`).

Call a single endpoint in the DevUI (REST wire):

```
POST http://localhost:5001/api/trame/json
{
  "controller": "Order",
  "method": "GetById",
  "params": [{ "parameterName": "id", "data": 42 }],
  "id": "q1"
}
```

## The Trame Way — one batch, declared dependencies

Paste this into the DevUI batch sender (`POST /api/trame/json/multi`). It is the whole screen
in **one roundtrip**: the client declares the graph, never extracts an id.

```jsonc
{
  "mode": 0,   // ExecutionMode: 0 = Parallel, 1 = Serial. Ignored here — a dependencyMapping
               //              is present, so the server auto-detects and runs the topological path.
  "requests": [
    { "controller": "Order",     "method": "GetById",      "id": "order",
      "params": [{ "parameterName": "id", "data": 42 }],
      "dependencyMapping": { "customerId": "$.customerId", "orderId": "$.id", "addressId": "$.shippingAddressId" } },

    { "controller": "Customer",  "method": "GetById",      "id": "customer",
      "params": [{ "parameterName": "customerId", "data": "@customerId" }] },

    { "controller": "OrderLine",  "method": "GetByOrder",   "id": "lines",
      "params": [{ "parameterName": "orderId", "data": "@orderId" }],
      "dependencyMapping": { "articleIds": "$[*].articleId" } },

    { "controller": "Article",   "method": "GetMany",      "id": "articles",
      "params": [{ "parameterName": "articleIds", "data": "@articleIds" }] },

    { "controller": "Stock",     "method": "GetByArticles", "id": "stock",
      "params": [{ "parameterName": "articleIds", "data": "@articleIds" }] },

    { "controller": "Address",   "method": "GetById",      "id": "address",
      "params": [{ "parameterName": "addressId", "data": "@addressId" }] }
  ]
}
```

### What the server does

```
                     Order.GetById(42)
                   exposes: customerId, orderId, addressId
              ┌──────────┬──────────────────┬───────────────┐
              ▼          ▼                  ▼               ▼
        Customer    OrderLine.GetByOrder  Address       (articleIds not yet available)
       (@customerId)  exposes: articleIds  (@addressId)
                         │          │
                         ▼          ▼
                    Article     Stock            ← diamond: one provider, two consumers
                  (@articleIds)(@articleIds)       both fed from the same `articleIds`
```

- `dependencyMapping` on a request says *"my result exposes this alias from this JsonPath"*
  (result-relative: `$` is the whole result, `$.customerId` a property, `$[*].articleId`
  a multi-match → one `List<int>`).
- `@alias` as a parameter value is a placeholder the server resolves from the prior result
  that exposed it. The client never reads an id.
- `Mode` is ignored the moment a `dependencyMapping` is present — the server auto-detects
  and runs the topological batch path.

### The REST Way (for contrast)

Six sequential roundtrips, the client extracting ids between each:

```csharp
var order    = await client.Call<Order?>    (TrameCall.Init("Order","GetById").With(42));
var customer = await client.Call<Customer?> (TrameCall.Init("Customer","GetById").With(order.CustomerId));
var lines    = await client.Call<List<OrderLine>>(TrameCall.Init("OrderLine","GetByOrder").With(order.Id));
var articleIds = lines.Select(l => l.ArticleId).Distinct().ToList();          // ← client glue
var articles = await client.Call<List<Article>>(TrameCall.Init("Article","GetMany").With(articleIds));
var address  = await client.Call<Address?>   (TrameCall.Init("Address","GetById").With(order.ShippingAddressId));
var stock    = await client.Call<List<StockInfo>>(TrameCall.Init("Stock","GetByArticles").With(articleIds)); // recomputed
```

|                          | The REST Way      | The Trame Way          |
|--------------------------|-------------------|------------------------|
| Roundtrips               | 6 (sequential)    | **1**                  |
| Client orchestration     | 6 calls + id glue | 1 batch, declared deps |
| Id extraction in client  | yes (manual)      | none (server binds)    |
| Workflow knowledge in    | every client      | the server (once)      |

## Where NOT to use this

- **Replacing an efficient SQL join.** If all reads are in one DB, a single query with joins
  beats service calls. Trame chains *across services you cannot or will not join in SQL*.
- **Unbounded graphs.** Chaining is for bounded intermediate results. Trame caps both sides
  (`MaxResultElementCount` default 10 000, `MaxParameterArrayLength` default 1 000).
- **CRUD that is genuinely CRUD.** One resource by id with no dependencies is already one
  roundtrip. Trame earns its keep when there is a *graph*, not a row.

## Files

- `Program.cs` — F5 wiring (`UseStaticWebAssets` + `AddTrame` + `UseTrameTransports` + `MapTrame`).
- `Domain.cs` — the six code-first controllers + in-memory store (Order #42, 30 ms/call simulated latency).
- Full narrative: `docs/stories/01-the-n-plus-one-screen.md`.

Next: **Story 02 — One Button, Seven Commands** (a write-side fan-out, per-command isolation).