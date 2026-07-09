# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Build the entire solution
dotnet build Hrpc.sln

# Run all tests
dotnet test HrpcTests/HrpcTests.csproj

# Run a specific test class
dotnet test HrpcTests/HrpcTests.csproj --filter "FullyQualifiedName~HrpcInvokerTests"

# Run a single test method
dotnet test HrpcTests/HrpcTests.csproj --filter "FullyQualifiedName~HrpcInvokerTests.MyTestMethod"

# Run benchmarks (Release mode required)
dotnet run -c Release --project HrpcBench/HrpcBench.csproj

# Run the sample application
dotnet run --project Hrpc/Hrpc.csproj
```

## Architecture

Hrpc is a **multi-transport RPC framework** for .NET 8 that is **code-first** (no `.proto` files, no IDL). The C# classes decorated with attributes *are* the contract. Discovery metadata is generated at runtime.

### Project Dependency Graph

```
HrpcCommon (shared models, attributes, exceptions)
    ↑
HrpcCore (invoker, discovery, dependency resolver, interceptors) ← depends on JsonPath.Net
    ↑
┌───────────┬──────────────┬──────────────┐
│ HrpcRest  │ HrpcWebSocket│  HrpcHub     │  ← transport layers
│ (Minimal  │ (RFC 6455    │  (SignalR +  │
│  APIs)    │  middleware)  │   MsgPack)   │
└───────────┴──────────────┴──────────────┘
    ↑              ↑              ↑
HrpcClient (REST, WebSocket, SignalR clients + fluent HrpcCall builder)
    ↑
Hrpc (sample app) · HrpcTests (xUnit + FluentAssertions) · HrpcBench (BenchmarkDotNet)
```

### Core Engine (`HrpcCore`)

**`HrpcInvoker`** (registered as singleton `IHrpcCore`) is the central execution engine:
- `Register<T>()` scans `[HrpcController]` and `[HrpcMethod]` attributes, compiles each method into an **Expression Tree delegate** (`Func<object, object?[], object?>`) stored in a `ConcurrentDictionary` cache. No reflection per call.
- `InvokeDi(HrpcRequest)` runs a single call through the interceptor pipeline → authorization → parameter deserialization → compiled delegate invocation → result serialization.
- `InvokeDi(IEnumerable<HrpcRequest>, ExecutionMode)` handles batch calls:
  - **Parallel**: `Task.WhenAll` over all requests.
  - **Serial**: sequential execution with `@alias` dependency resolution between calls.
  - **Auto-detect**: if any request has `DependencyMapping`, switches to topological batch execution via `DependencyGraphBuilder`.
- Handles `void`, `Task`, `Task<T>`, and `IAsyncEnumerable<T>` return types. Streaming results are consumed into a `List<T>` and serialized as a JSON array.

**`DependencyGraphBuilder`** performs topological sort (Kahn's algorithm) on requests with `@alias` placeholders. Groups independent requests into parallel batches; detects cycles.

**`IHrpcInterceptor`** / `HrpcInvocationDelegate`: middleware pipeline pattern. Interceptors wrap the actual execution in reverse registration order. Built-in `HrpcLoggingInterceptor` logs call duration.

**`HrpcDiscoveryService`** generates `DiscoveryInfo` from registered controllers, including `[HrpcDataContract]` types, `[HrpcDocumentation]` summaries, and `[HrpcExample]` JSON samples. Cached with invalidation on new registrations.

### Transports

| Transport | Project | Endpoint | Wire Format |
|-----------|---------|----------|-------------|
| REST | HrpcRest | `POST /api/hrpc/json`, `/api/hrpc/json/multi`, `GET /api/hrpc/discovery` | HTTP/1.1 + JSON |
| WebSocket | HrpcWebSocket | `ws://host/hrpcws` | RFC 6455 + JSON text frames |
| SignalR | HrpcHub | `/Hrpc` hub | WebSocket + MessagePack binary |

All transports deserialize the incoming `HrpcRequest`/`HrpcMultiRequest` and delegate to `IHrpcCore.InvokeDi()`.

### Client Library (`HrpcClient`)

- `IHrpcClient` interface with `Call(HrpcRequest)`, `Call<T>(HrpcRequest)`, `Call(HrpcMultiRequest)`.
- `HrpcClientBase`: shared `Call<T>()` with JSON deserialization and `HrpcException` on errors.
- `HrpcRestJsonClient`: `HttpClient`-based, `IDisposable`.
- `HrpcWebSocketClient`: persistent `ClientWebSocket` connection, `SemaphoreSlim` for thread safety, `IAsyncDisposable`.
- `HrpcSignalrClient`: SignalR hub connection with auto-reconnect and exponential backoff.
- `HrpcCall`: fluent builder — `.Init("Controller", "Method").With(args).Named("id").Exposes("$", "alias").WithAlias("@alias", "default").ToRequest()`. The `Exposes` JSON path is **result-relative** (`$` = whole result, `$.Property`, `$[0].Id`); there is no `$.data` envelope level.

### Key Attributes (defined in `HrpcCommon`)

- `[HrpcController("name")]` — marks a class as an RPC controller
- `[HrpcMethod("name")]` — marks a method as remotely callable
- `[HrpcAuthorise]` — requires authentication (optional role parameter)
- `[HrpcDataContract]` — marks a type for discovery metadata
- `[HrpcDocumentation("summary")]` — XML-like doc for discovery
- `[HrpcExample("json")]` — example JSON for developer UI

### Request/Response Model

Parameters are sent as `HrpcParameter[]` (name+data pairs), matched by parameter name. `CancellationToken` is injected automatically. `byte[]` parameters receive raw binary from `HrpcRequest.BinaryData`.

`HrpcResponse` carries `Code` (HTTP-like status), `Data` (JSON result), `Content` (binary), `ExposedDependencies` (for chaining), and `HrpcError` on failure. Clients throw `HrpcException` on non-2xx responses.

### Error Handling

Controllers signal non-success two ways — use the right one:
- **Business/domain errors → return `HrpcResponse`** via the `HrpcResults` factory (`HrpcCommon.Results`): `HrpcResults.NotFound("…")`, `HrpcResults.BadRequest("…")`, `HrpcResults.Error(code, message, details?)`, `HrpcResults.Ok(obj)`. The invoker passes a returned `HrpcResponse` through verbatim (`ReturnResponse`), so your `Code`+`Data`+`Error` reach the client. The message is **not** gated by `EnableDetailedErrors`.
- **Unexpected/internal failures → throw**. Any throw becomes a generic `500` (no message leak); the stack only lands in `error.details` when `EnableDetailedErrors` (Development) is on.
- Do **not** throw `HrpcException` to set a custom code — the server has no `catch(HrpcException)`; it becomes generic 500. Control the code via `HrpcResults.Error(...)`.

### Dependency Chaining

Request A declares `DependencyMapping: { "alias" → "$.Path" }` (a **result-relative** JsonPath — `$` is the whole serialized result, e.g. `$`, `$.Id`, `$[0].Id`; there is no `$.data` envelope level). After execution, values are extracted via `DependencyResolver.ExtractValue(result.Data, jsonPath)` (parsed against the result JSON) and stored in `ExposedDependencies`. Request B uses `@alias` as a parameter placeholder — the server resolves it before execution (`ResolveParameterValues` → `ReplaceDependencyByAlias`). Both the Serial (`ExecuteSequentially`) and the auto-detect topological-batch (`ExecuteInDependencyBatches`) paths resolve aliases against prior responses. This enables multiple dependent calls in a single roundtrip.

### Registration Flow

`AddHrpc(HrpcOptions)` in `HrpcHub`:
1. Registers SignalR (optional, with MessagePack support)
2. Configures rate limiting (fixed-window, configurable)
3. Registers `IHrpcCore` as singleton (`HrpcInvoker`)
4. Registers `HrpcLoggingInterceptor`
5. Auto-discovers all `[HrpcController]` types across assemblies and registers them as scoped services

`UseHrpc()` resolves `IHrpcCore` and triggers controller registration. `MapHrpcEndpoints()` (in `HrpcRest`) adds the REST endpoints.

### Test Project (`HrpcTests`)

Uses **xUnit** + **FluentAssertions** + **Moq**. Tests are organized as:
- `Unit/Core/` — `HrpcInvokerTests`, `DependencyGraphBuilderTests`, `DependencyResolverTests`, `HrpcDiscoveryServiceTests`
- `Unit/Client/` — `HrpcClientTests`
- `Integration/` — `RestTransportTests` (uses `WebApplicationFactory`)
- `Fixtures/` — test controller definitions

### Code Conventions

- The codebase uses **German** for comments, log messages, and error strings. New code should follow this convention.
- Controllers are registered via attribute scanning (`[HrpcController]`), not manual registration.
- The `HrpcInvoker` is a singleton — controllers are resolved per-call via `IServiceScopeFactory.CreateScope()`.
- Expression Tree compilation happens once at registration time, not per invocation.
