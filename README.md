# Trame

> **A code-first framework for building command-oriented Web APIs on .NET.**
>
> One contract. Multiple transports. **Server-side dependency chaining.**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Trame is designed for **command-oriented Web APIs**. Unlike resource-oriented frameworks, Trame models **commands** as the primary abstraction and allows dependent commands to exchange **typed JSON fragments** within a single request.

Your C# classes are the contract — no `.proto`, no IDL, no code generation. The same call runs over REST, WebSocket, or SignalR, consumable from any language. Discovery metadata is generated at runtime.

> **The name.** *Trame* (French, /tʁam/) — the weft, the cross-threads that hold a fabric together. Trame weaves multiple transports and chained calls into one framework.

> **Full reference:** [README_DETAILS.md](README_DETAILS.md) · **Wire format:** [PROTOCOL.md](PROTOCOL.md) · **Roadmap:** [ROADMAP.md](ROADMAP.md)

---

## Why Trame?

Business applications are built around commands: CreateOrder, ApproveInvoice, CancelBooking, AssignRole, GenerateInvoice.

REST models resources exceptionally well. Trame models **commands**.

---

## The missing piece

Traditional RPC executes one method after another. The client must: execute command A → parse the response → extract values → transform them → call command B → repeat.

Trame moves this orchestration to the server.

---

## Dependency Chaining

Dependency Chaining is Trame's defining feature. A command may expose **any JSON fragment** from its result:

```csharp
.Exposes("$.items[*].id", "customerIds")
```

A later command consumes it directly:

```csharp
.WithAlias("@customerIds")
```

Aliases are **typed JSON fragments**. They may represent primitive values, arrays, complex objects, or nested object graphs. A wildcard path (`$[*].id`) collects **all** matches into an array and injects it as one list-typed parameter — a server-side `Select`.

### Example

```csharp
var batch = new TrameMultiRequest
{
    Mode = ExecutionMode.Serial,
    Requests =
    [
        TrameCall.Init("Customer", "Search")
            .With("France")
            .Exposes("$.items[*].id", "customerIds")
            .ToRequest(),

        TrameCall.Init("Order", "GetOpenByCustomerIds")
            .WithAlias("@customerIds")
            .Exposes("$.items[*].articleId", "articleIds")
            .ToRequest(),

        TrameCall.Init("Article", "GetAvailability")
            .WithAlias("@articleIds")
            .ToRequest()
    ]
};
```

Three dependent commands. One request. No client-side orchestration.

---

## More than RPC

Trame is more expressive than traditional RPC because commands may exchange typed data directly. At the same time, Trame deliberately avoids GraphQL's additional complexity. There is no schema language, no resolver model, no query language, no code generation. Your C# classes are the contract.

## Why not GraphQL?

GraphQL answers: *Which object graph do you want?* Trame answers: *Which commands do you want to execute?* The client describes a dependency graph between commands rather than an object graph.

---

## Code First

```csharp
[TrameController("Customer")]
public class CustomerHandler
{
    [TrameMethod("Create")]
    public Task<int> Create(string name) => /* ... */ 42;
}
```

Runtime discovery generates the contract directly from your code.

---

## Runtime Discovery

`GET /api/trame/discovery` returns controllers, methods, parameter contracts, examples, and documentation — without maintaining a separate schema. A built-in developer UI (`/Trame`) lets you browse and try calls in the browser.

---

## Multi Transport

The same contract works over REST, WebSocket, and SignalR. Choose the transport. Not the programming model.

| Transport | Endpoint | Use case |
|-----------|----------|----------|
| REST | `POST /api/trame/json` | Stateless calls, load-balanced scenarios |
| WebSocket | `ws://host/tramews` | Persistent connections, low latency, many small calls |
| SignalR | `/tramehub` hub | Browser clients, auto-reconnect, MessagePack binary |

---

## Safety

Dependency chaining is resolved at runtime. If an exposed JSON fragment cannot be bound to the destination parameter, the request fails with a binding error.

To protect the server, Trame enforces **two configurable cardinality caps**, secure by default:

- **`MaxResultElementCount`** (default **10,000**) — caps the *producing* result (the source of a fan-out).
- **`MaxParameterArrayLength`** (default **1,000**) — caps the *consuming* parameter (the injected `@alias` array).

Either can be raised or disabled (`0` = unlimited). Dependency chaining is intended for bounded intermediate results, not unbounded server-side joins.

---

## Binary

A method may take or return `byte[]`. Bytes travel **out of band** from the JSON `data` field (`binaryData` request, `content` response).

| Transport | `binaryData` (request) | `content` (response) |
|-----------|------------------------|----------------------|
| REST (JSON) | base64 | base64 |
| WebSocket (JSON text frames) | base64 | base64 |
| SignalR (MessagePack) | native `bin` | native `bin` |

Limits: REST 1 MB body, WebSocket 1 MB/message, SignalR configurable. For large or frequent binary, run a plain REST/WebSocket endpoint alongside Trame. Streamed binary is not in v1 — see [ROADMAP.md](ROADMAP.md).

---

## Clients

- **C#** — `Trame.Client` NuGet: REST, WebSocket, SignalR behind one `ITrameClient`, fluent `TrameCall`, batches, chaining, binary. See [`TrameClient/README.md`](TrameClient/README.md).
- **TypeScript/JavaScript** — `npm i trame-client`: isomorphic REST + WebSocket, runs in browser and Node. See [`clients/ts/README.md`](clients/ts/README.md).
- **Other languages** — the wire protocol is JSON and fully specified in [PROTOCOL.md](PROTOCOL.md); any HTTP/WebSocket client works. `GET /api/trame/discovery` returns full type metadata for auto-generated clients.

---

## Features

- Command-oriented Web APIs
- Code-first (no `.proto`, no IDL, no code generation)
- Dependency chaining (scalar, array, object, nested graph)
- Runtime discovery + developer UI
- REST, WebSocket, SignalR — same call
- Batch execution (parallel, serial, topological)
- Streaming (`IAsyncEnumerable<T>`)
- Binary support
- Interceptor pipeline, authorization, rate-limiting
- Expression-tree invocation (no reflection per call)

---

## Quick Start

### Server

```csharp
using TrameHub.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register Trame with all transports
builder.Services.AddTrame(new TrameOptions
{
    UseSignalR = true,
    UseMessagePack = true,
    MaximumParallelInvocationsPerClient = 100
});

// Register your controllers (auto-discovered via [TrameController])
builder.Services.AddSingleton<MyService>();

var app = builder.Build();
app.UseTrame();                       // Trame REST endpoints
app.UseWebSockets();
app.UseTrameWebSocket("/tramews");    // WebSocket transport
app.MapHub<TrameHub.Hub.TrameHub>("/tramehub");  // SignalR transport
app.Run();
```

### Controller

```csharp
[TrameController("Customer")]
public class CustomerHandler(CustomerService service)
{
    [TrameMethod("GetById")]
    public async Task<Customer?> GetById(int id, CancellationToken ct)
        => await service.GetById(id, ct);

    [TrameMethod("Create")]
    [TrameAuthorise]  // Optional: requires authentication
    public async Task<int> Create(string name)
        => await service.Add(name);
}
```

### Client

```csharp
using TrameClient.Trame;

// Choose your transport
var client = new TrameRestJsonClient("https://localhost:5001/");
// or: new TrameWebSocketClient("https://localhost:5001/")
// or: new TrameSignalrClient("https://localhost:5001/")

// Single call
var request = TrameCall.Init("Customer", "GetById").With(42).ToRequest();
var customer = await client.Call<Customer>(request);

// Batch with dependency chaining
var batch = new TrameMultiRequest
{
    Mode = ExecutionMode.Serial,
    Requests = new List<TrameRequest>
    {
        TrameCall.Init("Customer", "Create")
            .With("Alice").Named("step1")
            .Exposes("$", "newId")
            .ToRequest(),
        TrameCall.Init("Customer", "GetById")
            .WithAlias("@newId")
            .Named("step2")
            .ToRequest()
    }
};
var responses = await client.Call(batch);
```

---

## Performance

Benchmarked with BenchmarkDotNet on .NET 8.0, Intel Core i7-12700H:

| Scenario | REST | Trame REST | Trame WebSocket | Speedup |
|----------|------:|----------:|---------------:|--------:|
| Single Call (GetCustomerById) | 606 µs | 627 µs | **267 µs** | **2.3x** |
| Batch 10x (parallel) | 4,906 µs | 974 µs | **693 µs** | **7.1x** |
| Dependency-Chain (3 calls, 1 roundtrip) | 206 ms | 205 ms | **204 ms** | replaces 3 roundtrips |

> Full benchmarks: [TrameBench/BENCHMARK-RESULTS.md](TrameBench/BENCHMARK-RESULTS.md)

---

## Known Limitations (v1)

Trame v1 is intentionally focused. These are deliberate scope decisions, not bugs:

- **No Native AOT** — runtime reflection + `Expression.Compile` are incompatible with AOT trimming.
- **No built-in API versioning** — encode version in the controller name (`Customer.v1`); source-generator enforcement planned for v1.1.
- **REST streaming is materialized** — `IAsyncEnumerable<T>` is consumed server-side into a JSON array; use WebSocket/SignalR for streaming semantics.
- **Attribute-based registration only** — controllers/methods via `[TrameController]`/`[TrameMethod]`; fluent registration is on the roadmap.
- **Binary is base64 over REST and WebSocket** — only SignalR/MessagePack carries `byte[]` natively; WebSocket honors text frames only.
- **Error details in development only** — `TrameError.details` is populated only with `EnableDetailedErrors` / Development.

> Full details, comparison tables, and project structure: [README_DETAILS.md](README_DETAILS.md). Roadmap: [ROADMAP.md](ROADMAP.md).

---

## Installation

```xml
<!-- Server: all transports -->
<PackageReference Include="Trame.All" Version="1.0.0" />

<!-- Or individual transports -->
<PackageReference Include="Trame.Core" Version="1.0.0" />
<PackageReference Include="Trame.Hub" Version="1.0.0" />
<PackageReference Include="Trame.Rest" Version="1.0.0" />
<PackageReference Include="Trame.WebSocket" Version="1.0.0" />

<!-- Client -->
<PackageReference Include="Trame.Client" Version="1.0.0" />
```

## Requirements

- .NET 8.0+
- ASP.NET Core 8.0+

## License

MIT