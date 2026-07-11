# Trame

**Trame** is a high-performance, multi-transport RPC framework for .NET 8 that combines the simplicity of REST with the performance of persistent connections.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

---

## The Problem with REST for RPC

REST is excellent for resource-oriented APIs — CRUD operations on nouns like `/customers/42` or `/orders`. But when you need to call *actions* (RPC-style), REST forces you into awkward patterns:

- **Verb-noun mismatch**: `POST /api/customer/42/add-address` is not RESTful, but it's what you actually need
- **N+1 query problem**: Need a customer, then their orders, then each order's items? That's 3 sequential HTTP roundtrips — the client waits for each response before sending the next request
- **No batching**: Want to call 10 methods in one go? REST gives you no way to do this — 10 separate HTTP requests, 10 separate connections, 10× the overhead
- **No dependency resolution**: Call A returns an ID that Call B needs? The client must manually parse A's response, extract the ID, and pass it to B — every time

GraphQL solves some of this with query composition, but brings its own complexity: schema-first design, resolvers, a separate type system, and it's still HTTP/1.1 under the hood.

## Why Not gRPC?

gRPC is the obvious choice for RPC on .NET — so why build something else?

1. **gRPC was not well-supported** when Trame was created. gRPC-Web (for browsers) required a proxy, native browser support was missing, and the .NET tooling was still immature.
2. **Schema-first is the wrong default for .NET developers.** gRPC requires `.proto` files, a code generator, and a separate build step. You write your schema in a DSL, generate C# stubs, then implement them. In .NET, your C# classes *already are* the schema — why maintain a second definition in a different language?
3. **gRPC locks you to one transport.** HTTP/2 + Protobuf is great for server-to-server, but browsers need gRPC-Web, and you can't use it over plain WebSocket or REST without a separate implementation.

Trame is **code-first**: you write C# classes and methods, decorate them with attributes, and the framework handles the rest. No `.proto` files, no code generation, no separate schema. Discovery metadata is generated at runtime from your code.

## How Does Trame Compare to JSON-RPC?

[JSON-RPC 2.0](https://www.jsonrpc.org/) is the closest spiritual ancestor to Trame — both are method-oriented, not resource-oriented, and both use JSON. So why not just use JSON-RPC?

| | JSON-RPC 2.0 | **Trame** |
|---|---|---|
| Method-oriented calling | ✅ | ✅ |
| Batch calls | ✅ (JSON array) | ✅ |
| Dependency-chaining | ❌ calls are independent | ✅ `@alias` resolution |
| Type contracts | ❌ untyped `params` | ✅ signature inference (`[TrameDataContract]` optional) |
| API discovery | ❌ (needs OpenRPC) | ✅ built-in `/api/trame/discovery` |
| Documentation | ❌ (external) | ✅ `[TrameDocumentation]` + `[TrameExample]` |
| Streaming | ❌ | ✅ `IAsyncEnumerable<T>` |
| Authorization | ❌ (transport-level) | ✅ `[TrameAuthorise]` |
| Error model | ✅ basic (`code` + `message`) | ✅ `TrameError` with details + requestId |
| .NET integration | ❌ (libraries exist, but no framework) | ✅ native ASP.NET Core middleware |
| Transport definitions | ❌ (spec is format-only) | ✅ REST + WebSocket + SignalR defined |
| Pre-compiled invocation | ❌ (reflection) | ✅ Expression Trees |

**In short**: JSON-RPC defines a *wire format*, Trame is a *framework*. JSON-RPC tells you how to format the JSON, but doesn't help you discover methods, resolve dependencies, stream results, authorize calls, or integrate with .NET DI. Trame was built without knowing JSON-RPC existed, but ended up solving the same core problem (REST is wrong for RPC) with significantly more functionality.

## Why Trame?

| | REST | gRPC | GraphQL | JSON-RPC | **Trame** |
|---|---|---|---|---|---|
| Designed for RPC | ❌ | ✅ | partial | ✅ | ✅ |
| Code-first (no IDL) | ✅ | ❌ (`.proto`) | ❌ (schema) | ✅ | ✅ |
| Browser-native (no proxy) | ✅ | ❌ (gRPC-Web) | ✅ | ✅ | ✅ |
| Multi-transport | ❌ | ❌ | ❌ | format-only | ✅ REST + WebSocket + SignalR |
| Batch-calls | ❌ | ❌ | ✅ | ✅ | ✅ |
| Dependency-chaining | ❌ | ❌ | ✅ | ❌ | ✅ |
| Type contracts | ❌ | ✅ | ✅ | ❌ | ✅ |
| Streaming | ❌ | ✅ | ✅ | ❌ | ✅ `IAsyncEnumerable<T>` |
| Interceptor-pipeline | ❌ | ✅ | ❌ | ❌ | ✅ |
| API-Discovery | OpenAPI | ❌ | Introspection | ❌ | ✅ built-in |
| Developer-UI | Swagger | ❌ | GraphiQL | ❌ | ✅ |
| Rate-limiting | manual | ❌ | ❌ | ❌ | ✅ built-in |

Trame was designed from the ground up for RPC — not as a REST wrapper, but as a proper RPC framework that happens to support REST as one of several transports:

1. **Method-oriented, not resource-oriented**: `[TrameController("Customer")]` + `[TrameMethod("AddOrder")]` — no URL routing, no HTTP verbs to choose, no DTO mapping
2. **Batch calls**: Send 10 RPC calls in one roundtrip — the server executes them and returns 10 responses
3. **Dependency chaining** (GraphQL-inspired): One request exposes values via `Exposes("$", "orderId")` (result-relative JSON path), the next uses `WithAlias("@orderId")` — the server resolves dependencies in a single roundtrip
4. **Multi-transport**: REST for compatibility, WebSocket for low-latency persistent connections, SignalR for browser clients — same API, different wire protocols
5. **Expression-Tree invocation**: Methods are pre-compiled to delegates at registration time — no reflection per call

| | REST | gRPC | SignalR | **Trame** |
|---|---|---|---|---|
| Multi-Transport | no | no | no | **yes** (REST + WebSocket + SignalR) |
| Batch-Calls | no | no | no | **yes** |
| Dependency-Chaining | no | no | no | **yes** |
| Streaming | no | yes | yes | **yes** (IAsyncEnumerable<T>) |
| Interceptor-Pipeline | no | yes | no | **yes** |
| API-Discovery | OpenAPI | no | no | **yes** (built-in) |
| Developer-UI | Swagger | no | no | **yes** |
| Rate-Limiting | manual | no | no | **yes** (built-in) |

## Performance

Benchmarked with BenchmarkDotNet on .NET 8.0, Intel Core i7-12700H:
| Scenario | REST | Trame REST | Trame WebSocket | Speedup |
|----------|------:|----------:|---------------:|--------:|
| Single Call (GetCustomerById) | 606 us | 627 us | **267 us** | **2.3x** |
| Batch 10x (parallel) | 4,906 us | 974 us | **693 us** | **7.1x** |
| Dependency-Chain (3 calls, 1 roundtrip) | 206 ms | 205 ms | **204 ms** | replaces 3 roundtrips |

> Full benchmarks: see TrameBench/BENCHMARK-RESULTS.md

## Quick Start

### Server

`csharp
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
app.UseTrame();              // Registers Trame REST endpoints
app.UseWebSockets();
app.UseTrameWebSocket("/tramews");  // WebSocket transport
app.MapHub<TrameHub.Hub.TrameHub>("/Trame");  // SignalR transport
app.Run();
`

### Controller

`csharp
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
`

### Client

`csharp
using TrameClient.Trame;

// Choose your transport
var client = new TrameRestJsonClient("https://localhost:5001/");
// or: new TrameWebSocketClient("https://localhost:5001/")
// or: new TrameSignalrClient("https://localhost:5001/")

// Single call
var request = TrameCall.Init("Customer", "GetById")
    .With(42)
    .ToRequest();
var response = await client.Call(request);

// Typed call
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
`

## Transports

| Transport | Endpoint | Use Case |
|-----------|----------|----------|
| **REST** | POST /api/trame/json | Stateless calls, load-balanced scenarios |
| **WebSocket** | ws://host/tramews | Persistent connections, low latency, many small calls |
| **SignalR** | /Trame hub | Browser clients, auto-reconnect, MessagePack binary |

## Features

- **Multi-Transport**: Same API over REST, WebSocket, and SignalR
- **Batch-Calls**: Execute multiple RPC calls in a single roundtrip (parallel or serial)
- **Dependency-Chaining**: Chain calls with @alias placeholders resolved server-side
- **Streaming**: IAsyncEnumerable<T> support, serialized as JSON arrays
- **Interceptor-Pipeline**: ITrameInterceptor for logging, tracing, caching, validation
- **Discovery/MEX**: GET /api/trame/discovery returns full API metadata
- **Developer-UI**: Built-in web UI at /developer for browsing and testing APIs
- **Rate-Limiting**: Built-in fixed-window rate limiter
- **Authorization**: [TrameAuthorise] attribute with role-based access
- **Expression-Tree Invocation**: Pre-compiled method delegates for maximum performance

## Cross-Platform

Trame's wire protocol is fully specified in [PROTOCOL.md](PROTOCOL.md) — JSON-based, no binary dependency. This enables implementations in any language:

- **TypeScript/JavaScript**: A ready-to-use isomorphic client ships in [`clients/ts/`](clients/ts/) (`npm i trame-client`) — REST + WebSocket, fluent + functional API, runs in the browser and Node.js. See [`clients/ts/README.md`](clients/ts/README.md).
- **Python**: Use the REST endpoint with `requests`, or `websockets` library
- **Go/Rust**: Any HTTP or WebSocket client works

The `/api/trame/discovery` endpoint returns full type metadata, enabling auto-generated clients.

> The wire format and a minimal hand-rolled example are documented in [PROTOCOL.md](PROTOCOL.md); for real use prefer the `clients/ts/` package.

## Known Limitations (v1)

Trame v1 is intentionally focused. The following are deliberate scope decisions, not bugs — documented here so adopters can plan around them:

- **No built-in API versioning.** The `controller` field is a free-form string. There is no framework-level versioning mechanism. The recommended convention is to encode the version in the controller name, e.g. `[TrameController("Customer.v1")]`, so a v2 can coexist without breaking v1 clients. Build-time version enforcement via a source generator (the `wsdl.exe` model) is planned for v1.1 — see [ROADMAP.md](ROADMAP.md).
- **No Native AOT.** Method discovery uses runtime reflection, and invocation uses `Expression.Compile`-produced delegates. This is incompatible with Native AOT trimming. Standard (JIT) publishing is the supported deployment model.
- **REST streaming is materialized.** An `IAsyncEnumerable<T>` result is fully consumed server-side and serialized as a JSON array before the REST response is sent. True streamed delivery over REST is not supported — use the WebSocket or SignalR transport for streaming semantics.
- **Attribute-based registration only.** Controllers and methods are discovered via `[TrameController]` / `[TrameMethod]` attribute scanning. Direct/fluent registration of individual handlers in code (without attributes) is on the roadmap, not in v1.
- **Dotted namespaces, not URL hierarchies.** The `controller` field accepts a dotted namespace (e.g. `Customer.Address.Contact`) to express arbitrarily deep groupings, but it is a single string key — it is not translated into a nested URL path. Routing stays two-part (`controller` + `method`) at the protocol level.
- **Error details in development only.** `TrameError.details` (stack trace) is populated only when `EnableDetailedErrors` is set or the host runs in Development. In production, error messages are intentionally generic and do not leak exception internals.

## Project Structure

| Project | Description |
|---------|-------------|
| TrameCommon | Shared models, attributes, exceptions |
| TrameCore | Invoker, discovery, dependency resolver, interceptors |
| TrameHub | SignalR transport + AddTrame() / UseTrame() extensions |
| TrameRest | REST transport (Minimal APIs + MVC controller) |
| TrameWebSocket | WebSocket transport (RFC 6455, JSON text frames) |
| TrameClient | Client library (REST, WebSocket, SignalR clients) |
| TrameDeveloperUi | Built-in developer web UI |
| Trame | Sample application |
| TrameTests | Unit + integration tests |
| TrameBench | Performance benchmarks |

## Installation (NuGet)

`xml
<!-- Server: all transports -->
<PackageReference Include="Trame.All" Version="1.0.0" />

<!-- Or individual transports -->
<PackageReference Include="Trame.Core" Version="1.0.0" />
<PackageReference Include="Trame.Hub" Version="1.0.0" />
<PackageReference Include="Trame.Rest" Version="1.0.0" />
<PackageReference Include="Trame.WebSocket" Version="1.0.0" />

<!-- Client -->
<PackageReference Include="Trame.Client" Version="1.0.0" />
`

## Requirements

- .NET 8.0+
- ASP.NET Core 8.0+

## License

MIT
