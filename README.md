# WaevaRpc

**WaevaRpc** is a high-performance, multi-transport RPC framework for .NET 8 that combines the simplicity of REST with the performance of persistent connections.

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

1. **gRPC was not well-supported** when WaevaRpc was created. gRPC-Web (for browsers) required a proxy, native browser support was missing, and the .NET tooling was still immature.
2. **Schema-first is the wrong default for .NET developers.** gRPC requires `.proto` files, a code generator, and a separate build step. You write your schema in a DSL, generate C# stubs, then implement them. In .NET, your C# classes *already are* the schema — why maintain a second definition in a different language?
3. **gRPC locks you to one transport.** HTTP/2 + Protobuf is great for server-to-server, but browsers need gRPC-Web, and you can't use it over plain WebSocket or REST without a separate implementation.

WaevaRpc is **code-first**: you write C# classes and methods, decorate them with attributes, and the framework handles the rest. No `.proto` files, no code generation, no separate schema. Discovery metadata is generated at runtime from your code.

## How Does WaevaRpc Compare to JSON-RPC?

[JSON-RPC 2.0](https://www.jsonrpc.org/) is the closest spiritual ancestor to WaevaRpc — both are method-oriented, not resource-oriented, and both use JSON. So why not just use JSON-RPC?

| | JSON-RPC 2.0 | **WaevaRpc** |
|---|---|---|
| Method-oriented calling | ✅ | ✅ |
| Batch calls | ✅ (JSON array) | ✅ |
| Dependency-chaining | ❌ calls are independent | ✅ `@alias` resolution |
| Type contracts | ❌ untyped `params` | ✅ `[WaevaRpcDataContract]` |
| API discovery | ❌ (needs OpenRPC) | ✅ built-in `/api/waeva/discovery` |
| Documentation | ❌ (external) | ✅ `[WaevaRpcDocumentation]` + `[WaevaRpcExample]` |
| Streaming | ❌ | ✅ `IAsyncEnumerable<T>` |
| Authorization | ❌ (transport-level) | ✅ `[WaevaRpcAuthorise]` |
| Error model | ✅ basic (`code` + `message`) | ✅ `WaevaRpcError` with details + requestId |
| .NET integration | ❌ (libraries exist, but no framework) | ✅ native ASP.NET Core middleware |
| Transport definitions | ❌ (spec is format-only) | ✅ REST + WebSocket + SignalR defined |
| Pre-compiled invocation | ❌ (reflection) | ✅ Expression Trees |

**In short**: JSON-RPC defines a *wire format*, WaevaRpc is a *framework*. JSON-RPC tells you how to format the JSON, but doesn't help you discover methods, resolve dependencies, stream results, authorize calls, or integrate with .NET DI. WaevaRpc was built without knowing JSON-RPC existed, but ended up solving the same core problem (REST is wrong for RPC) with significantly more functionality.

## Why WaevaRpc?

| | REST | gRPC | GraphQL | JSON-RPC | **WaevaRpc** |
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

WaevaRpc was designed from the ground up for RPC — not as a REST wrapper, but as a proper RPC framework that happens to support REST as one of several transports:

1. **Method-oriented, not resource-oriented**: `[WaevaRpcController("Customer")]` + `[WaevaRpcMethod("AddOrder")]` — no URL routing, no HTTP verbs to choose, no DTO mapping
2. **Batch calls**: Send 10 RPC calls in one roundtrip — the server executes them and returns 10 responses
3. **Dependency chaining** (GraphQL-inspired): One request exposes values via `Exposes("$", "orderId")` (result-relative JSON path), the next uses `WithAlias("@orderId")` — the server resolves dependencies in a single roundtrip
4. **Multi-transport**: REST for compatibility, WebSocket for low-latency persistent connections, SignalR for browser clients — same API, different wire protocols
5. **Expression-Tree invocation**: Methods are pre-compiled to delegates at registration time — no reflection per call

| | REST | gRPC | SignalR | **WaevaRpc** |
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
| Scenario | REST | WaevaRpc REST | WaevaRpc WebSocket | Speedup |
|----------|------:|----------:|---------------:|--------:|
| Single Call (GetCustomerById) | 606 us | 627 us | **267 us** | **2.3x** |
| Batch 10x (parallel) | 4,906 us | 974 us | **693 us** | **7.1x** |
| Dependency-Chain (3 calls, 1 roundtrip) | 206 ms | 205 ms | **204 ms** | replaces 3 roundtrips |

> Full benchmarks: see WaevaRpcBench/BENCHMARK-RESULTS.md

## Quick Start

### Server

`csharp
using WaevaRpcHub.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register WaevaRpc with all transports
builder.Services.AddWaevaRpc(new WaevaRpcOptions
{
    UseSignalR = true,
    UseMessagePack = true,
    MaximumParallelInvocationsPerClient = 100
});

// Register your controllers (auto-discovered via [WaevaRpcController])
builder.Services.AddSingleton<MyService>();

var app = builder.Build();
app.UseWaevaRpc();              // Registers WaevaRpc REST endpoints
app.UseWebSockets();
app.UseWaevaRpcWebSocket("/waevaws");  // WebSocket transport
app.MapHub<WaevaRpcHub.Hub.WaevaRpcHub>("/Waeva");  // SignalR transport
app.Run();
`

### Controller

`csharp
[WaevaRpcController("Customer")]
public class CustomerHandler(CustomerService service)
{
    [WaevaRpcMethod("GetById")]
    public async Task<Customer?> GetById(int id, CancellationToken ct)
        => await service.GetById(id, ct);

    [WaevaRpcMethod("Create")]
    [WaevaRpcAuthorise]  // Optional: requires authentication
    public async Task<int> Create(string name)
        => await service.Add(name);
}
`

### Client

`csharp
using WaevaRpcClient.WaevaRpc;

// Choose your transport
var client = new WaevaRpcRestJsonClient("https://localhost:5001/");
// or: new WaevaRpcWebSocketClient("https://localhost:5001/")
// or: new WaevaRpcSignalrClient("https://localhost:5001/")

// Single call
var request = WaevaRpcCall.Init("Customer", "GetById")
    .With(42)
    .ToRequest();
var response = await client.Call(request);

// Typed call
var customer = await client.Call<Customer>(request);

// Batch with dependency chaining
var batch = new WaevaRpcMultiRequest
{
    Mode = ExecutionMode.Serial,
    Requests = new List<WaevaRpcRequest>
    {
        WaevaRpcCall.Init("Customer", "Create")
            .With("Alice").Named("step1")
            .Exposes("$", "newId")
            .ToRequest(),
        WaevaRpcCall.Init("Customer", "GetById")
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
| **REST** | POST /api/waeva/json | Stateless calls, load-balanced scenarios |
| **WebSocket** | ws://host/waevaws | Persistent connections, low latency, many small calls |
| **SignalR** | /Waeva hub | Browser clients, auto-reconnect, MessagePack binary |

## Features

- **Multi-Transport**: Same API over REST, WebSocket, and SignalR
- **Batch-Calls**: Execute multiple RPC calls in a single roundtrip (parallel or serial)
- **Dependency-Chaining**: Chain calls with @alias placeholders resolved server-side
- **Streaming**: IAsyncEnumerable<T> support, serialized as JSON arrays
- **Interceptor-Pipeline**: IWaevaRpcInterceptor for logging, tracing, caching, validation
- **Discovery/MEX**: GET /api/waeva/discovery returns full API metadata
- **Developer-UI**: Built-in web UI at /developer for browsing and testing APIs
- **Rate-Limiting**: Built-in fixed-window rate limiter
- **Authorization**: [WaevaRpcAuthorise] attribute with role-based access
- **Expression-Tree Invocation**: Pre-compiled method delegates for maximum performance

## Cross-Platform

WaevaRpc's wire protocol is fully specified in [PROTOCOL.md](PROTOCOL.md) — JSON-based, no binary dependency. This enables implementations in any language:

- **TypeScript/JavaScript**: A ready-to-use isomorphic client ships in [`clients/ts/`](clients/ts/) (`npm i waeva-rpc-client`) — REST + WebSocket, fluent + functional API, runs in the browser and Node.js. See [`clients/ts/README.md`](clients/ts/README.md).
- **Python**: Use the REST endpoint with `requests`, or `websockets` library
- **Go/Rust**: Any HTTP or WebSocket client works

The `/api/waeva/discovery` endpoint returns full type metadata, enabling auto-generated clients.

> The wire format and a minimal hand-rolled example are documented in [PROTOCOL.md](PROTOCOL.md); for real use prefer the `clients/ts/` package.

## Known Limitations (v1)

WaevaRpc v1 is intentionally focused. The following are deliberate scope decisions, not bugs — documented here so adopters can plan around them:

- **No built-in API versioning.** The `controller` field is a free-form string. There is no framework-level versioning mechanism. The recommended convention is to encode the version in the controller name, e.g. `[WaevaRpcController("Customer.v1")]`, so a v2 can coexist without breaking v1 clients. Build-time version enforcement via a source generator (the `wsdl.exe` model) is planned for v1.1 — see [ROADMAP.md](ROADMAP.md).
- **No Native AOT.** Method discovery uses runtime reflection, and invocation uses `Expression.Compile`-produced delegates. This is incompatible with Native AOT trimming. Standard (JIT) publishing is the supported deployment model.
- **REST streaming is materialized.** An `IAsyncEnumerable<T>` result is fully consumed server-side and serialized as a JSON array before the REST response is sent. True streamed delivery over REST is not supported — use the WebSocket or SignalR transport for streaming semantics.
- **Attribute-based registration only.** Controllers and methods are discovered via `[WaevaRpcController]` / `[WaevaRpcMethod]` attribute scanning. Direct/fluent registration of individual handlers in code (without attributes) is on the roadmap, not in v1.
- **Dotted namespaces, not URL hierarchies.** The `controller` field accepts a dotted namespace (e.g. `Customer.Address.Contact`) to express arbitrarily deep groupings, but it is a single string key — it is not translated into a nested URL path. Routing stays two-part (`controller` + `method`) at the protocol level.
- **Error details in development only.** `WaevaRpcError.details` (stack trace) is populated only when `EnableDetailedErrors` is set or the host runs in Development. In production, error messages are intentionally generic and do not leak exception internals.

## Project Structure

| Project | Description |
|---------|-------------|
| WaevaRpcCommon | Shared models, attributes, exceptions |
| WaevaRpcCore | Invoker, discovery, dependency resolver, interceptors |
| WaevaRpcHub | SignalR transport + AddWaevaRpc() / UseWaevaRpc() extensions |
| WaevaRpcRest | REST transport (Minimal APIs + MVC controller) |
| WaevaRpcWebSocket | WebSocket transport (RFC 6455, JSON text frames) |
| WaevaRpcClient | Client library (REST, WebSocket, SignalR clients) |
| WaevaRpcDeveloperUi | Built-in developer web UI |
| WaevaRpc | Sample application |
| WaevaRpcTests | Unit + integration tests |
| WaevaRpcBench | Performance benchmarks |

## Installation (NuGet)

`xml
<!-- Server: all transports -->
<PackageReference Include="WaevaRpc.All" Version="1.0.0" />

<!-- Or individual transports -->
<PackageReference Include="WaevaRpc.Core" Version="1.0.0" />
<PackageReference Include="WaevaRpc.Hub" Version="1.0.0" />
<PackageReference Include="WaevaRpc.Rest" Version="1.0.0" />
<PackageReference Include="WaevaRpc.WebSocket" Version="1.0.0" />

<!-- Client -->
<PackageReference Include="WaevaRpc.Client" Version="1.0.0" />
`

## Requirements

- .NET 8.0+
- ASP.NET Core 8.0+

## License

MIT
