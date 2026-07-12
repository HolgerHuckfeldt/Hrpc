# Trame

> **Detailed reference.** For the landing page, see [README.md](README.md).

**Trame** is a code-first, multi-transport framework for **command-oriented web APIs** on .NET 8+. Your C# classes *are* the contract; discovery metadata is generated at runtime — no `.proto`, no IDL. The same call runs over REST, WebSocket, or SignalR, and is consumable from any language, not .NET only.

What sets Trame apart is **dependency chaining**: send several calls in a single roundtrip and let later calls reuse results from earlier ones. One request exposes a value via `Exposes("$", "newId")` (a result-relative JSON path), the next consumes it as `WithAlias("@newId")`, and the server resolves the `@alias` placeholders against prior responses — no client-side glue code, no extra roundtrips. A workflow that needs a customer's new ID to create their order completes in one roundtrip instead of three.

> **The name.** *Trame* (French, /tʁam/) is the weft — the cross-threads in weaving that hold a fabric together. Trame weaves multiple transports (REST, WebSocket, SignalR) and chained calls into one coherent framework.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

---

## Resources, Commands, and Trame's direction

REST is so much *the* standard that "wrong" is the wrong word for it — it simply isn't shaped for command calls. For resource-oriented APIs, CRUD on nouns like `/customers/42` or `/orders` is exactly right. **Are REST, gRPC, or GraphQL wrong, then? No — each was built for a shape of API it handles well.** Trame simply takes a different direction: a *command-oriented* web API where the following come up naturally:

- **Verb-noun mismatch**: `POST /api/customer/42/add-address` isn't RESTful, but it's what a command-oriented API actually needs
- **N+1 roundtrips for dependent data**: a customer, then their orders, then each order's items is 3 sequential HTTP roundtrips — the client waits on each before sending the next
- **No batching**: 10 method calls means 10 separate HTTP requests and connections
- **No dependency resolution**: when call A returns an ID that call B needs, the client manually parses A, extracts the ID, and passes it to B — every time

GraphQL addresses composition with a schema, resolvers, and a separate type system — and does it well, at the cost of that schema-first layer.

None of this is a knock on REST. Its ubiquity forced everyone — for a generation — to think in terms of a *web API*: resources, verbs, status codes, content negotiation, stateless scaling. That discipline was extremely helpful and is now second nature. The friction above is what shows up when that same resource-oriented shape is pressed into *command-oriented* work.

Trame doesn't strip the web API away — it keeps it. Calls travel over HTTP with JSON, through standard ASP.NET Core middleware, and the contract is consumable from any language, not just .NET. To honor that web-API layer rather than treat it as plumbing, Trame ships **runtime discovery** (`GET /api/trame/discovery` returns the full contract: controllers, methods, parameter schemas, documentation, examples) and a **developer UI** that lets you browse and try calls in the browser. That makes Trame a discoverable, language-neutral, command-oriented web API — not a closed channel between two .NET ends. What it drops is only the resource orientation: commands stop having to masquerade as nouns.

## Trame vs gRPC — a different direction

gRPC is a strong, sensible choice for RPC on .NET. Trame isn't a rejection of it — it takes a different direction on three axes:

1. **History.** When Trame started, gRPC-Web (for browsers) required a proxy, native browser support was missing, and the .NET tooling was still immature. That's no longer true today; it's the historical reason Trame exists.
2. **Code-first, not schema-first.** gRPC requires `.proto` files, a code generator, and a separate build step — you write the schema in a DSL, generate C# stubs, then implement them. That's a deliberate, well-justified choice in gRPC. Trame goes the other way: in .NET your C# classes *already are* the schema, so it keeps a single definition and generates discovery metadata at runtime.
3. **Transport choice.** gRPC standardizes on one wire — HTTP/2 + Protobuf, great for server-to-server — and reaches browsers via gRPC-Web. Trame lets the *same* call run over REST, WebSocket, or SignalR, so you pick the transport per deployment, not per call.

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
| .NET integration | partial (StreamJsonRpc & libs) | ✅ native ASP.NET Core middleware |
| Transport definitions | ❌ (spec is format-only) | ✅ REST + WebSocket + SignalR defined |
| Pre-compiled invocation | ❌ (reflection) | ✅ Expression Trees |

**In short**: JSON-RPC defines a *wire format*, Trame is a *framework*. JSON-RPC tells you how to format the JSON, but doesn't help you discover methods, resolve dependencies, stream results, authorize calls, or integrate with .NET DI. Trame was built without knowing JSON-RPC existed, but ended up solving a similar core problem — a command-oriented web API on .NET — with a different emphasis: discovery, dependency chaining, streaming, authorization, and DI integration built into the framework rather than left to the caller.

## Why Trame?

| | REST | gRPC | GraphQL | JSON-RPC | **Trame** |
|---|---|---|---|---|---|
| Designed for RPC | ❌ | ✅ | partial | ✅ | ✅ |
| Code-first (no IDL) | ✅ | ❌ (`.proto`) | ❌ (schema) | ✅ | ✅ |
| Browser-native (no proxy) | ✅ | ❌ (gRPC-Web) | ✅ | ✅ | ✅ |
| Multi-transport | ❌ | partial (gRPC-Web via proxy) | ✅ HTTP + WS subscriptions | format-only | ✅ same call over REST + WebSocket + SignalR |
| Batch-calls | ❌ | ❌ | ✅ | ✅ | ✅ |
| Dependency-chaining | ❌ | ❌ | ✅ | ❌ | ✅ |
| Type contracts | ❌ | ✅ | ✅ | ❌ | ✅ |
| Streaming | ❌ | ✅ | ✅ | ❌ | ✅ `IAsyncEnumerable<T>` |
| Interceptor-pipeline | ❌ | ✅ | ✅ (HTTP/socket + field middleware) | ❌ | ✅ |
| API-Discovery | OpenAPI | ✅ (reflection) | Introspection | ❌ | ✅ built-in |
| Developer-UI | Swagger | grpcui | GraphiQL | ❌ | ✅ |
| Rate-limiting | manual | ❌ | partial (concurrency + cost) | ❌ | ✅ built-in |

> **On transport, honestly:** GraphQL is not HTTP-only — the spec (and Hot Chocolate on .NET) supports subscriptions over WebSocket, and gRPC reaches browsers via gRPC-Web. The distinction is *what the transports carry*. In GraphQL, HTTP serves queries/mutations and WebSocket serves subscriptions — different operation types on different channels. In Trame the **same** call runs over REST, WebSocket, or SignalR with identical wire semantics; you pick the transport per deployment, not per operation. Trame's actual differentiator is **dependency chaining** (above), not multi-transport.

Trame was designed from the ground up as a **command-oriented web API** — not a REST wrapper, but a proper framework for command-oriented APIs that happens to support REST as one of several transports:

1. **Method-oriented, not resource-oriented**: `[TrameController("Customer")]` + `[TrameMethod("AddOrder")]` — no URL routing, no HTTP verbs to choose, no DTO mapping
2. **Batch calls**: Send 10 calls in one roundtrip — the server executes them and returns 10 responses
3. **Dependency chaining** (GraphQL-inspired): One request exposes values via `Exposes("$", "orderId")` (result-relative JSON path), the next uses `WithAlias("@orderId")` — the server resolves dependencies in a single roundtrip. A wildcard path `Exposes("$[*].Id", "ids")` collects **all** matches into an array and injects it as one list-typed parameter — `Search → GetByIds(@ids)` in a single roundtrip, like a server-side `Select`
4. **Multi-transport**: REST for compatibility, WebSocket for low-latency persistent connections, SignalR for browser clients — same API, different wire protocols
5. **Expression-Tree invocation**: Methods are pre-compiled to delegates at registration time — no reflection per call

| | REST | gRPC | SignalR | **Trame** |
|---|---|---|---|---|
| Multi-Transport | no | partial (gRPC-Web) | no | **yes** (REST + WebSocket + SignalR) |
| Batch-Calls | no | no | no | **yes** |
| Dependency-Chaining | no | no | no | **yes** |
| Streaming | no | yes | yes | **yes** (IAsyncEnumerable<T>) |
| Interceptor-Pipeline | no | yes | yes (hub filters) | **yes** |
| API-Discovery | OpenAPI | yes (reflection) | no | **yes** (built-in) |
| Developer-UI | Swagger | grpcui | no | **yes** |
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

```csharp
using TrameHub.Extensions;
using TrameServer;

var builder = WebApplication.CreateBuilder(args);

// Register Trame with all transports (hub + REST + WebSocket + DevUI)
builder.Services.AddTrame(o =>
{
    o.UseSignalR = true;
    o.UseMessagePack = true;
    o.MaximumParallelInvocationsPerClient = 100;
});

var app = builder.Build();
app.UseTrameTransports();   // WebSocket + controller registration
app.MapTrame();             // REST (/api/trame) + DevUI (/Trame) + SignalR hub (/tramehub)
app.Run();
```

### Controller

```csharp
using TrameCore.Attributes;

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
using TrameCommon.Models;

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
```

## Transports

| Transport | Endpoint | Use Case |
|-----------|----------|----------|
| **REST** | POST /api/trame/json | Stateless calls, load-balanced scenarios |
| **WebSocket** | ws://host/tramews | Persistent connections, low latency, many small calls |
| **SignalR** | /tramehub hub | Browser clients, auto-reconnect, MessagePack binary |

## Features

- **Multi-Transport**: Same API over REST, WebSocket, and SignalR
- **Batch-Calls**: Execute multiple calls in a single roundtrip (parallel or serial)
- **Dependency-Chaining**: Chain calls with @alias placeholders resolved server-side; wildcard paths (`$[*].Id`) fan a result set into a single list-typed parameter
- **Streaming**: IAsyncEnumerable<T> support, serialized as JSON arrays
- **Interceptor-Pipeline**: ITrameInterceptor for logging, tracing, caching, validation
- **Discovery/MEX**: GET /api/trame/discovery returns full API metadata
- **Developer-UI**: Built-in web UI at /Trame for browsing and testing APIs
- **Rate-Limiting**: Built-in fixed-window rate limiter
- **Authorization**: [TrameAuthorise] attribute with role-based access
- **Expression-Tree Invocation**: Pre-compiled method delegates for maximum performance

## Binary

A method may take a `byte[]` parameter or return `byte[]`. The bytes travel **out of band from the JSON `data` field** — request bytes in `binaryData`, response bytes in `content` — so they never compete with structured arguments and are never duplicated into `data`. The wire encoding depends on the transport:

| Transport | `binaryData` (request) | `content` (response) |
|---|---|---|
| REST (JSON) | base64 in JSON | base64 in JSON |
| WebSocket (JSON text frames) | base64 in JSON | base64 in JSON |
| SignalR (MessagePack) | native MessagePack `bin` | native MessagePack `bin` |

Base64 is avoided **only on the SignalR/MessagePack channel**. REST and WebSocket carry JSON text, so binary is base64-encoded there (~33 % overhead), bounded by the message-size limits. The WebSocket transport deliberately accepts only text frames — native binary frames are not honored.

**Limits:** REST 1 MB request body; WebSocket 1 MB per message (hardcoded); SignalR `MaximumReceiveMessageSize` in `TrameOptions` (defaults to SignalR's limit when unset). The cardinality caps `MaxParameterArrayLength` / `MaxResultElementCount` do not apply to `byte[]` — they guard collection sizes, not binary size.

**Practical guidance:** Binary over REST or WebSocket is fine for payloads up to the limits. For large or frequent binary, run a plain REST or WebSocket endpoint alongside Trame — there is nothing about binary that requires the RPC channel. Streamed/chunked binary upload and a streamed `byte[]` response are not in v1; see [ROADMAP.md](ROADMAP.md) for the v1.x+ binary-transfer plan.

**Client support:** TypeScript ships `withBinary(Uint8Array)` and `callBinary()`; SignalR clients (C# + TS) carry `byte[]` natively. The C# REST/WebSocket client offers `CallBinary()` for responses, but the fluent builder has **no `WithBinary` yet** — set `request.BinaryData` directly until it lands. See [PROTOCOL.md](PROTOCOL.md) for exact field semantics.

## Cross-Platform

Trame's wire protocol is fully specified in [PROTOCOL.md](PROTOCOL.md) — JSON-based, no binary dependency. This enables implementations in any language:

- **C#**: A first-class client ships as the `Trame.Client` NuGet package — REST, WebSocket, and SignalR behind one `ITrameClient` surface, fluent `TrameCall` builder, batches, dependency chaining, binary. See [`TrameClient/README.md`](TrameClient/README.md).
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
- **Binary is base64 over REST and WebSocket.** Only the SignalR/MessagePack channel carries `byte[]` without base64; the WebSocket transport honors text frames only. Binary is bounded by the transport message-size limits (REST 1 MB, WebSocket 1 MB). For large or frequent binary, run a plain REST or WebSocket endpoint alongside Trame — see [Binary](#binary) and the v1.x+ plan in [ROADMAP.md](ROADMAP.md).
- **`byte[]` parameters bind first-match-only.** A method with more than one `byte[]` parameter receives `binaryData` in only the first; the others are not filled.
- **No streamed binary.** `byte[]` responses are buffered into `content`; a `ContentStream` field exists on the model but is not wired by any transport in v1.
- **C# fluent builder has no `WithBinary`.** Set `request.BinaryData` directly for binary uploads from C# until the helper lands (see [ROADMAP.md](ROADMAP.md)).

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
| TrameTelemetry | Optional OpenTelemetry SDK bootstrap (subscribes the `Trame` source, wires OTLP/Console exporters + AspNetCore/HttpClient instrumentation) |
| Trame | Sample application |
| TrameTests | Unit + integration tests |
| TrameBench | Performance benchmarks |

## Installation (NuGet)

```xml
<!-- Server: all transports (hub + REST + WebSocket + DevUI) -->
<PackageReference Include="Trame.Server" Version="1.0.0" />

<!-- Optional: OpenTelemetry SDK bootstrap -->
<PackageReference Include="Trame.Telemetry" Version="1.0.0" />

<!-- Or individual transports -->
<PackageReference Include="Trame.Core" Version="1.0.0" />
<PackageReference Include="Trame.Hub" Version="1.0.0" />
<PackageReference Include="Trame.Rest" Version="1.0.0" />
<PackageReference Include="Trame.WebSocket" Version="1.0.0" />

<!-- Client -->
<PackageReference Include="Trame.Client" Version="1.0.0" />
```

### Distributed Tracing

Trame instruments the engine directly in `TrameCore` via a public `ActivitySource` named **`Trame`** (`TrameCore.Tracing.TrameTracing.ActivitySourceName`). The instrumentation is always on but cost-neutral — `ActivitySource.StartActivity` returns `null` when nothing subscribes, so there is no allocation or bookkeeping unless a listener (the OTel SDK or a custom `ActivityListener`) is attached. No NuGet dependency is added to `TrameCore`/`TrameServer` for this; `System.Diagnostics` is in-box for .NET 8.

Spans emitted (OTel RPC semantic conventions):

- **`TrameCall`** — one per request, started in both the single-call path (`InvokeDi(TrameRequest)`) and the per-request batch path (`ExecuteSingleInvocation`). Tags: `rpc.system=trame`, `rpc.service`, `rpc.method`, `trame.request_id` (only when `Id` is non-empty), `trame.binary.length` (only when `BinaryData` is non-empty). Status set from the response (`Ok` when `IsSuccess`, else `Error` with `Error.Message`). On an escaping exception the catch adds `exception.type`/`exception.message`/`exception.stacktrace` (via `TrameTracing.RecordException`, which replaces `Activity.RecordException` — the extension is not resolvable in a net8.0 class library).
- **`TrameBatch`** — one per batch, started in `InvokeDi(IEnumerable<TrameRequest>)`. Tags: `rpc.system=trame`, `trame.batch.mode` (`Parallel`/`Serial`, or `DependencyBatches` when auto-detect routes to topological execution), `trame.batch.count`. The per-request `TrameCall` spans are children via `Activity.Current` parenting — a single batch yields one parent + N children.

`Trame.Telemetry` is the optional package that boots the OTel SDK: `AddTrameTelemetry` calls `AddOpenTelemetry().WithTracing(b => b.AddSource("Trame") …)` with configurable service name, OTLP/Console exporter, and AspNetCore/HttpClient instrumentation gates. `TrameServer` does **not** reference it, keeping the OTel SDK dependencies out of the all-in-one bundle. Consumers who need custom samplers/resources/exporters skip `AddTrameTelemetry` and call `AddOpenTelemetry().WithTracing(b => b.AddSource("Trame"))` directly — the source name is the only integration point.

## Requirements

- .NET 8.0+
- ASP.NET Core 8.0+

## License

MIT