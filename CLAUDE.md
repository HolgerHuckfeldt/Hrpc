# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Build the entire solution
dotnet build Trame.sln

# Run all tests
dotnet test TrameTests/TrameTests.csproj

# Run a specific test class
dotnet test TrameTests/TrameTests.csproj --filter "FullyQualifiedName~TrameInvokerTests"

# Run a single test method
dotnet test TrameTests/TrameTests.csproj --filter "FullyQualifiedName~TrameInvokerTests.MyTestMethod"

# Run benchmarks (Release mode required)
dotnet run -c Release --project TrameBench/TrameBench.csproj

# Run the sample application
dotnet run --project Trame/Trame.csproj
```

## Architecture

Trame is a **multi-transport RPC framework** for .NET 8 that is **code-first** (no `.proto` files, no IDL). The C# classes decorated with attributes *are* the contract. Discovery metadata is generated at runtime.

### Project Dependency Graph

```
TrameCommon (shared models, attributes, exceptions)
    ↑
TrameCore (invoker, discovery, dependency resolver, interceptors) ← depends on JsonPath.Net
    ↑
┌───────────┬──────────────┬──────────────┐
│ TrameRest  │ TrameWebSocket│  TrameHub     │  ← transport layers
│ (Minimal  │ (RFC 6455    │  (SignalR +  │
│  APIs)    │  middleware)  │   MsgPack)   │
└───────────┴──────────────┴──────────────┘
    ↑              ↑              ↑
TrameClient (REST, WebSocket, SignalR clients + fluent TrameCall builder)
    ↑
Trame (sample app) · TrameTests (xUnit + FluentAssertions) · TrameBench (BenchmarkDotNet)
```

### Core Engine (`TrameCore`)

**`TrameInvoker`** (registered as singleton `ITrameCore`) is the central execution engine:
- `Register<T>()` scans `[TrameController]` and `[TrameMethod]` attributes, compiles each method into an **Expression Tree delegate** (`Func<object, object?[], object?>`) stored in a `ConcurrentDictionary` cache. No reflection per call.
- `InvokeDi(TrameRequest)` runs a single call through the interceptor pipeline → authorization → parameter deserialization → compiled delegate invocation → result serialization.
- `InvokeDi(IEnumerable<TrameRequest>, ExecutionMode)` handles batch calls:
  - **Parallel**: `Task.WhenAll` over all requests.
  - **Serial**: sequential execution with `@alias` dependency resolution between calls.
  - **Auto-detect**: if any request has `DependencyMapping`, switches to topological batch execution via `DependencyGraphBuilder`.
- Handles `void`, `Task`, `Task<T>`, and `IAsyncEnumerable<T>` return types. Streaming results are consumed into a `List<T>` and serialized as a JSON array.

**`DependencyGraphBuilder`** performs topological sort (Kahn's algorithm) on requests with `@alias` placeholders. Groups independent requests into parallel batches; detects cycles.

**`ITrameInterceptor`** / `TrameInvocationDelegate`: middleware pipeline pattern. Interceptors wrap the actual execution in reverse registration order. Built-in `TrameLoggingInterceptor` logs call duration.

**`TrameDiscoveryService`** generates `DiscoveryInfo` from registered controllers, including contract types, `[TrameDocumentation]` summaries, and `[TrameExample]` JSON samples. Contract types are **inferred from method signatures** by default (Weg C): any class type whose assembly belongs to the registered controllers' assemblies is fully expanded (property schema, example, nested types); types from other assemblies (BCL, framework envelope, third-party) stay opaque unless overridden. `[TrameDataContract]` is an optional override (bare = force-expand, `Exclude = true` = force-opaque). Cached with invalidation on new registrations.

### Transports

| Transport | Project | Endpoint | Wire Format |
|-----------|---------|----------|-------------|
| REST | TrameRest | `POST /api/trame/json`, `/api/trame/json/multi`, `GET /api/trame/discovery` | HTTP/1.1 + JSON |
| WebSocket | TrameWebSocket | `ws://host/tramews` | RFC 6455 + JSON text frames |
| SignalR | TrameHub | `/tramehub` hub | WebSocket + MessagePack binary |

All transports deserialize the incoming `TrameRequest`/`TrameMultiRequest` and delegate to `ITrameCore.InvokeDi()`.

### Client Library (`TrameClient`)

- `ITrameClient` interface with `Call(TrameRequest)`, `Call<T>(TrameRequest)`, `Call(TrameMultiRequest)`.
- `TrameClientBase`: shared `Call<T>()` with JSON deserialization and `TrameException` on errors.
- `TrameRestJsonClient`: `HttpClient`-based, `IDisposable`.
- `TrameWebSocketClient`: persistent `ClientWebSocket` connection, `SemaphoreSlim` for thread safety, `IAsyncDisposable`.
- `TrameSignalrClient`: SignalR hub connection with auto-reconnect and exponential backoff.
- `TrameCall`: fluent builder — `.Init("Controller", "Method").With(args).Named("id").Exposes("$", "alias").WithAlias("@alias", "default").ToRequest()`. The `Exposes` JSON path is **result-relative** (`$` = whole result, `$.Property`, `$[0].Id`); there is no `$.data` envelope level.

### Key Attributes (defined in `TrameCommon`)

- `[TrameController("name")]` — marks a class as an RPC controller
- `[TrameMethod("name")]` — marks a method as remotely callable
- `[TrameAuthorise]` — requires authentication (optional role parameter)
- `[TrameDataContract]` — optional discovery override: bare = force-expand a type (e.g. a third-party type you want documented); `Exclude = true` = force-opaque. By default, discovery infers contract types from method signatures via the controller-assembly boundary.
- `[TrameDocumentation("summary")]` — XML-like doc for discovery
- `[TrameExample("json")]` — example JSON for developer UI

### Request/Response Model

Parameters are sent as `TrameParameter[]` (name+data pairs), matched by parameter name. `CancellationToken` is injected automatically. `byte[]` parameters receive raw binary from `TrameRequest.BinaryData`.

`TrameResponse` carries `Code` (HTTP-like status), `Data` (JSON result), `Content` (binary), `ExposedDependencies` (for chaining), and `TrameError` on failure. Clients throw `TrameException` on non-2xx responses.

### Error Handling

Controllers signal non-success two ways — use the right one:
- **Business/domain errors → return `TrameResponse`** via the `TrameResults` factory (`TrameCommon.Results`): `TrameResults.NotFound("…")`, `TrameResults.BadRequest("…")`, `TrameResults.Error(code, message, details?)`, `TrameResults.Ok(obj)`. The invoker passes a returned `TrameResponse` through verbatim (`ReturnResponse`), so your `Code`+`Data`+`Error` reach the client. The message is **not** gated by `EnableDetailedErrors`.
- **Unexpected/internal failures → throw**. Any throw becomes a generic `500` (no message leak); the stack only lands in `error.details` when `EnableDetailedErrors` (Development) is on.
- Do **not** throw `TrameException` to set a custom code — the server has no `catch(TrameException)`; it becomes generic 500. Control the code via `TrameResults.Error(...)`.

### Dependency Chaining

Request A declares `DependencyMapping: { "alias" → "$.Path" }` (a **result-relative** JsonPath — `$` is the whole serialized result, e.g. `$`, `$.Id`, `$[0].Id`; there is no `$.data` envelope level). After execution, values are extracted via `DependencyResolver.ExtractValue(result.Data, jsonPath)` (parsed against the result JSON) and stored in `ExposedDependencies`. `ExtractValue` is **match-count-aware**: a single-match path yields that match (scalar, or an array if the match itself is one); a multi-match path (`$[*].Id`, `$..Id`) collects all matches into a `JsonArray`, injected as one list-typed parameter (`List<T>`/`T[]`/`IEnumerable<T>`) — list fan-out into a *parameter*, never fan-out into N *requests*. Request B uses `@alias` as a parameter placeholder — the server resolves it before execution (`ResolveParameterValues` → `ReplaceDependencyByAlias`). Both the Serial (`ExecuteSequentially`) and the auto-detect topological-batch (`ExecuteInDependencyBatches`) paths resolve aliases against prior responses. This enables multiple dependent calls in a single roundtrip.

### Registration Flow

`AddTrame(TrameOptions)` in `TrameHub`:
1. Registers SignalR (optional, with MessagePack support)
2. Configures rate limiting (fixed-window, configurable)
3. Registers `ITrameCore` as singleton (`TrameInvoker`)
4. Registers `TrameLoggingInterceptor`
5. Auto-discovers all `[TrameController]` types across assemblies and registers them as scoped services

`UseTrame()` resolves `ITrameCore` and triggers controller registration. `MapTrameEndpoints()` (in `TrameRest`) adds the REST endpoints.

### Test Project (`TrameTests`)

Uses **xUnit** + **FluentAssertions** + **Moq**. Tests are organized as:
- `Unit/Core/` — `TrameInvokerTests`, `DependencyGraphBuilderTests`, `DependencyResolverTests`, `TrameDiscoveryServiceTests`
- `Unit/Client/` — `TrameClientTests`
- `Integration/` — `RestTransportTests` (uses `WebApplicationFactory`)
- `Fixtures/` — test controller definitions

### Code Conventions

- The codebase uses **German** for comments, log messages, and error strings. New code should follow this convention.
- Controllers are registered via attribute scanning (`[TrameController]`), not manual registration.
- The `TrameInvoker` is a singleton — controllers are resolved per-call via `IServiceScopeFactory.CreateScope()`.
- Expression Tree compilation happens once at registration time, not per invocation.
