# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Build the entire solution
dotnet build WaevaRpc.sln

# Run all tests
dotnet test WaevaRpcTests/WaevaTests.csproj

# Run a specific test class
dotnet test WaevaRpcTests/WaevaTests.csproj --filter "FullyQualifiedName~WaevaRpcInvokerTests"

# Run a single test method
dotnet test WaevaRpcTests/WaevaTests.csproj --filter "FullyQualifiedName~WaevaRpcInvokerTests.MyTestMethod"

# Run benchmarks (Release mode required)
dotnet run -c Release --project WaevaRpcBench/WaevaBench.csproj

# Run the sample application
dotnet run --project WaevaRpc/Waeva.csproj
```

## Architecture

WaevaRpc is a **multi-transport RPC framework** for .NET 8 that is **code-first** (no `.proto` files, no IDL). The C# classes decorated with attributes *are* the contract. Discovery metadata is generated at runtime.

### Project Dependency Graph

```
WaevaRpcCommon (shared models, attributes, exceptions)
    ↑
WaevaRpcCore (invoker, discovery, dependency resolver, interceptors) ← depends on JsonPath.Net
    ↑
┌───────────┬──────────────┬──────────────┐
│ WaevaRpcRest  │ WaevaRpcWebSocket│  WaevaRpcHub     │  ← transport layers
│ (Minimal  │ (RFC 6455    │  (SignalR +  │
│  APIs)    │  middleware)  │   MsgPack)   │
└───────────┴──────────────┴──────────────┘
    ↑              ↑              ↑
WaevaRpcClient (REST, WebSocket, SignalR clients + fluent WaevaRpcCall builder)
    ↑
WaevaRpc (sample app) · WaevaRpcTests (xUnit + FluentAssertions) · WaevaRpcBench (BenchmarkDotNet)
```

### Core Engine (`WaevaRpcCore`)

**`WaevaRpcInvoker`** (registered as singleton `IWaevaRpcCore`) is the central execution engine:
- `Register<T>()` scans `[WaevaRpcController]` and `[WaevaRpcMethod]` attributes, compiles each method into an **Expression Tree delegate** (`Func<object, object?[], object?>`) stored in a `ConcurrentDictionary` cache. No reflection per call.
- `InvokeDi(WaevaRpcRequest)` runs a single call through the interceptor pipeline → authorization → parameter deserialization → compiled delegate invocation → result serialization.
- `InvokeDi(IEnumerable<WaevaRpcRequest>, ExecutionMode)` handles batch calls:
  - **Parallel**: `Task.WhenAll` over all requests.
  - **Serial**: sequential execution with `@alias` dependency resolution between calls.
  - **Auto-detect**: if any request has `DependencyMapping`, switches to topological batch execution via `DependencyGraphBuilder`.
- Handles `void`, `Task`, `Task<T>`, and `IAsyncEnumerable<T>` return types. Streaming results are consumed into a `List<T>` and serialized as a JSON array.

**`DependencyGraphBuilder`** performs topological sort (Kahn's algorithm) on requests with `@alias` placeholders. Groups independent requests into parallel batches; detects cycles.

**`IWaevaRpcInterceptor`** / `WaevaRpcInvocationDelegate`: middleware pipeline pattern. Interceptors wrap the actual execution in reverse registration order. Built-in `WaevaRpcLoggingInterceptor` logs call duration.

**`WaevaRpcDiscoveryService`** generates `DiscoveryInfo` from registered controllers, including `[WaevaRpcDataContract]` types, `[WaevaRpcDocumentation]` summaries, and `[WaevaRpcExample]` JSON samples. Cached with invalidation on new registrations.

### Transports

| Transport | Project | Endpoint | Wire Format |
|-----------|---------|----------|-------------|
| REST | WaevaRpcRest | `POST /api/waeva/json`, `/api/waeva/json/multi`, `GET /api/waeva/discovery` | HTTP/1.1 + JSON |
| WebSocket | WaevaRpcWebSocket | `ws://host/waevaws` | RFC 6455 + JSON text frames |
| SignalR | WaevaRpcHub | `/Waeva` hub | WebSocket + MessagePack binary |

All transports deserialize the incoming `WaevaRpcRequest`/`WaevaRpcMultiRequest` and delegate to `IWaevaRpcCore.InvokeDi()`.

### Client Library (`WaevaRpcClient`)

- `IWaevaRpcClient` interface with `Call(WaevaRpcRequest)`, `Call<T>(WaevaRpcRequest)`, `Call(WaevaRpcMultiRequest)`.
- `WaevaRpcClientBase`: shared `Call<T>()` with JSON deserialization and `WaevaRpcException` on errors.
- `WaevaRpcRestJsonClient`: `HttpClient`-based, `IDisposable`.
- `WaevaRpcWebSocketClient`: persistent `ClientWebSocket` connection, `SemaphoreSlim` for thread safety, `IAsyncDisposable`.
- `WaevaRpcSignalrClient`: SignalR hub connection with auto-reconnect and exponential backoff.
- `WaevaRpcCall`: fluent builder — `.Init("Controller", "Method").With(args).Named("id").Exposes("$", "alias").WithAlias("@alias", "default").ToRequest()`. The `Exposes` JSON path is **result-relative** (`$` = whole result, `$.Property`, `$[0].Id`); there is no `$.data` envelope level.

### Key Attributes (defined in `WaevaRpcCommon`)

- `[WaevaRpcController("name")]` — marks a class as an RPC controller
- `[WaevaRpcMethod("name")]` — marks a method as remotely callable
- `[WaevaRpcAuthorise]` — requires authentication (optional role parameter)
- `[WaevaRpcDataContract]` — marks a type for discovery metadata
- `[WaevaRpcDocumentation("summary")]` — XML-like doc for discovery
- `[WaevaRpcExample("json")]` — example JSON for developer UI

### Request/Response Model

Parameters are sent as `WaevaRpcParameter[]` (name+data pairs), matched by parameter name. `CancellationToken` is injected automatically. `byte[]` parameters receive raw binary from `WaevaRpcRequest.BinaryData`.

`WaevaRpcResponse` carries `Code` (HTTP-like status), `Data` (JSON result), `Content` (binary), `ExposedDependencies` (for chaining), and `WaevaRpcError` on failure. Clients throw `WaevaRpcException` on non-2xx responses.

### Error Handling

Controllers signal non-success two ways — use the right one:
- **Business/domain errors → return `WaevaRpcResponse`** via the `WaevaRpcResults` factory (`WaevaRpcCommon.Results`): `WaevaRpcResults.NotFound("…")`, `WaevaRpcResults.BadRequest("…")`, `WaevaRpcResults.Error(code, message, details?)`, `WaevaRpcResults.Ok(obj)`. The invoker passes a returned `WaevaRpcResponse` through verbatim (`ReturnResponse`), so your `Code`+`Data`+`Error` reach the client. The message is **not** gated by `EnableDetailedErrors`.
- **Unexpected/internal failures → throw**. Any throw becomes a generic `500` (no message leak); the stack only lands in `error.details` when `EnableDetailedErrors` (Development) is on.
- Do **not** throw `WaevaRpcException` to set a custom code — the server has no `catch(WaevaRpcException)`; it becomes generic 500. Control the code via `WaevaRpcResults.Error(...)`.

### Dependency Chaining

Request A declares `DependencyMapping: { "alias" → "$.Path" }` (a **result-relative** JsonPath — `$` is the whole serialized result, e.g. `$`, `$.Id`, `$[0].Id`; there is no `$.data` envelope level). After execution, values are extracted via `DependencyResolver.ExtractValue(result.Data, jsonPath)` (parsed against the result JSON) and stored in `ExposedDependencies`. Request B uses `@alias` as a parameter placeholder — the server resolves it before execution (`ResolveParameterValues` → `ReplaceDependencyByAlias`). Both the Serial (`ExecuteSequentially`) and the auto-detect topological-batch (`ExecuteInDependencyBatches`) paths resolve aliases against prior responses. This enables multiple dependent calls in a single roundtrip.

### Registration Flow

`AddWaevaRpc(WaevaRpcOptions)` in `WaevaRpcHub`:
1. Registers SignalR (optional, with MessagePack support)
2. Configures rate limiting (fixed-window, configurable)
3. Registers `IWaevaRpcCore` as singleton (`WaevaRpcInvoker`)
4. Registers `WaevaRpcLoggingInterceptor`
5. Auto-discovers all `[WaevaRpcController]` types across assemblies and registers them as scoped services

`UseWaevaRpc()` resolves `IWaevaRpcCore` and triggers controller registration. `MapWaevaRpcEndpoints()` (in `WaevaRpcRest`) adds the REST endpoints.

### Test Project (`WaevaRpcTests`)

Uses **xUnit** + **FluentAssertions** + **Moq**. Tests are organized as:
- `Unit/Core/` — `WaevaRpcInvokerTests`, `DependencyGraphBuilderTests`, `DependencyResolverTests`, `WaevaRpcDiscoveryServiceTests`
- `Unit/Client/` — `WaevaRpcClientTests`
- `Integration/` — `RestTransportTests` (uses `WebApplicationFactory`)
- `Fixtures/` — test controller definitions

### Code Conventions

- The codebase uses **German** for comments, log messages, and error strings. New code should follow this convention.
- Controllers are registered via attribute scanning (`[WaevaRpcController]`), not manual registration.
- The `WaevaRpcInvoker` is a singleton — controllers are resolved per-call via `IServiceScopeFactory.CreateScope()`.
- Expression Tree compilation happens once at registration time, not per invocation.
