# WaevaRpc Architecture

> German version: [ARCHITECTURE.de.md](ARCHITECTURE.de.md)

## Design Philosophy

WaevaRpc was born from a simple observation: **REST is designed for resources, not for RPC.**

When calling actions (not CRUD on nouns), REST forces awkward patterns:
- URL paths that pretend to be nouns: `/api/customer/42/add-address`
- N+1 roundtrips for dependent calls: client waits for A, then calls B, then C
- No batching: 10 method calls = 10 HTTP requests with 10× overhead

WaevaRpc takes inspiration from **GraphQL** (dependency resolution, batch queries) and **gRPC** (method-oriented calling, binary transport) but stays within the .NET/ASP.NET Core ecosystem.

### Why Not gRPC?

Two key reasons WaevaRpc exists instead of just using gRPC:

1. **gRPC was not well-supported** at creation time. Browser support required gRPC-Web proxies, and .NET gRPC tooling was still immature. WaevaRpc supports REST, WebSocket, and SignalR out of the box — browsers can use WebSocket or REST without proxies.

2. **Code-first, not schema-first.** gRPC requires `.proto` files — a separate IDL that generates C# stubs. This means maintaining a second source of truth: the `.proto` and the C# implementation. WaevaRpc uses attributes on plain C# classes: `[WaevaRpcController]`, `[WaevaRpcMethod]`, `[WaevaRpcDataContract]`. The C# code *is* the contract. Discovery metadata is generated at runtime, not compile time.

The core design principles:
1. **Method-oriented**: Controllers and methods, not routes and verbs
2. **Batch by default**: Multiple calls in one roundtrip, parallel or serial
3. **Server-side dependency resolution**: `@alias` chaining inspired by GraphQL field selection
4. **Transport-agnostic**: The same `WaevaRpcRequest` works over REST, WebSocket, or SignalR
5. **Zero-reflection invocation**: Expression Trees compile method calls at startup
6. **Code-first**: No IDL, no `.proto` files, no code generation — C# classes are the schema

## Overview

WaevaRpc is a protocol-agnostic RPC engine that sits between your business logic and multiple transport layers. The core engine (`WaevaRpcCore`) resolves method calls via pre-compiled Expression Trees, while transports (REST, WebSocket, SignalR) handle the wire protocol.

```
┌──────────────────────────────────────────────────────────┐
│                      Client                                │
│  WaevaRpcCall.Init("Ctrl", "Method").With(args).ToRequest()   │
│         │                                                  │
│    ┌────┴────┬────────────┬──────────────┐                 │
│    │ REST    │ WebSocket  │  SignalR     │  Transports     │
│    │ Client  │  Client   │  Client      │                 │
│    └────┬────┴────┬───────┴──────┬──────┘                 │
└─────────┼─────────┼──────────────┼────────────────────────┘
          │         │              │
          ▼         ▼              ▼
┌──────────────────────────────────────────────────────────┐
│                    Server (ASP.NET Core)                   │
│  ┌──────────┐ ┌─────────────┐ ┌────────────┐               │
│  │ WaevaRpcRest │ │ WaevaRpcWebSocket│ │ WaevaRpcHub   │  Transports   │
│  │ Endpoint │ │  Middleware  │ │ (SignalR) │               │
│  └────┬─────┘ └──────┬──────┘ └─────┬────┘               │
│       └──────────────┼───────────────┘                     │
│                      ▼                                     │
│        ┌─────────────────────────────┐                    │
│        │      IWaevaRpcCore (Invoker)      │  Core             │
│        │  ┌─────────────────────────┐  │                    │
│        │  │  Expression-Tree Call   │  │                    │
│        │  │  Parameter Resolution   │  │                    │
│        │  │  Authorization Check    │  │                    │
│        │  │  Interceptor Pipeline   │  │                    │
│        │  │  Dependency Resolver    │  │                    │
│        │  │  Discovery Service     │  │                    │
│        │  └─────────────────────────┘  │                    │
│        └──────────┬──────────────────┘                    │
│                   ▼                                        │
│        ┌─────────────────────────────┐                    │
│        │   [WaevaRpcController] classes   │  User Code         │
│        │   with [WaevaRpcMethod] methods  │                    │
│        └─────────────────────────────┘                    │
└────────────────────────────────────────────────────────────┘
```

## Request/Response Flow

### Single Call
1. Client creates `WaevaRpcRequest` with controller name, method name, and JSON parameters
2. Transport sends the request (HTTP POST, WebSocket frame, or SignalR hub invocation)
3. `WaevaRpcInvoker` looks up the controller and method in the invoke cache
4. Interceptor pipeline runs (logging, tracing, etc.)
5. Authorization is checked (`[WaevaRpcAuthorise]`)
6. Parameters are deserialized from JSON and matched by name
7. Method is invoked via pre-compiled Expression Tree delegate
8. Result is serialized to JSON and wrapped in `WaevaRpcResponse`
9. Response is sent back through the transport

### Batch Call (Multi-Request)
1. Client sends `WaevaRpcMultiRequest` with multiple `WaevaRpcRequest` items and an `ExecutionMode`
2. If any request has `DependencyMapping`, auto-detection switches to topological batch execution
3. **Parallel mode**: All requests execute concurrently via `Task.WhenAll`
4. **Serial mode**: Requests execute sequentially with dependency resolution
5. **Dependency-Batch mode**: `DependencyGraphBuilder` creates execution batches:
   - Level 0: All requests without dependencies → parallel
   - Level 1: Requests depending only on Level 0 results → parallel
   - Level N: Requests depending on Level N-1 results → parallel
6. Each level runs in parallel, levels run sequentially

### Dependency Chaining
1. Request A declares `DependencyMapping: { "alias" → "$" }` (result-relative JSON path; `$` is the whole result, `$.Property` a property, `$[0].Id` a list element)
   - After execution, the server extracts values from A's response using JsonPath
   - Extracted values are stored in `ExposedDependencies: { "alias" → value }`
2. Request B uses `@alias` as a parameter placeholder
   - The server replaces `@alias` with the actual value from A's `ExposedDependencies`
3. This enables 3 dependent calls in a single HTTP roundtrip

## Core Components

### WaevaRpcInvoker (`WaevaRpcCore`)
- Singleton service, thread-safe
- `ConcurrentDictionary` caches: controller types and compiled `InvokeInfo`
- `CompileInvocation()`: Expression Trees create `Func<object, object?[], object?>` per method
- `BuildParameters()`: Deserializes JSON parameters, matches by parameter name, injects `CancellationToken`
- `ExecuteMethod()`: Creates DI scope, resolves controller instance, invokes compiled delegate
- Handles sync, async (Task/Task<T>), void, and `IAsyncEnumerable<T>` return types

### WaevaRpcDiscoveryService (`WaevaRpcCore`)
- Generates `DiscoveryInfo` with all controllers, methods, parameters, and types
- Contract types are **inferred from method signatures**: any class type from a controller assembly is fully expanded (property schema, example, nested types); `[WaevaRpcDataContract]` is an optional override (bare = force-expand, `Exclude = true` = force-opaque). Types from other assemblies (BCL, framework envelope, third-party) stay opaque.
- Extracts `[WaevaRpcDocumentation]` summaries and `[WaevaRpcExample]` JSON examples
- Cached with invalidation on new registrations

### DependencyGraphBuilder (`WaevaRpcCore`)
- Topological sort of requests based on `DependencyMapping` and `@alias` usage
- Groups independent requests into parallel batches (level-based execution)
- Cycle detection throws `InvalidOperationException` with involved request IDs

### Interceptor Pipeline (`WaevaRpcCore`)
- `IWaevaRpcInterceptor`: `InvokeAsync(request, next, ct)`
- Pipeline wraps the actual method execution in reverse order
- Built-in: `WaevaRpcLoggingInterceptor` (traces call duration)
- Custom interceptors registered via DI: `services.AddSingleton<IWaevaRpcInterceptor, MyInterceptor>()`

### Transports

| Transport | Project | Wire Protocol | Key Features |
|-----------|---------|---------------|--------------|
| REST | WaevaRpcRest | HTTP/1.1 + JSON | Minimal APIs at `/api/waeva/json`, `/api/waeva/json/multi`, `/api/waeva/discovery` |
| WebSocket | WaevaRpcWebSocket | RFC 6455 + JSON text frames | Auto-detects single vs. multi-request, multi-frame support |
| SignalR | WaevaRpcHub | WebSocket + MessagePack | Hub methods `DoWork()` / `DoWorkMany()`, auto-reconnect |

### Client Library (`WaevaRpcClient`)
- `IWaevaRpcClient` interface: `Call(WaevaRpcRequest)`, `Call<T>(WaevaRpcRequest)`, `Call(WaevaRpcMultiRequest)`
- `WaevaRpcRestJsonClient`: HTTP-based, connection pooling, `IDisposable`
- `WaevaRpcWebSocketClient`: Persistent connection, `SemaphoreSlim` for thread safety, `IAsyncDisposable`
- `WaevaRpcSignalrClient`: Auto-reconnect with exponential backoff, MessagePack protocol
- `WaevaRpcCall`: Fluent builder with `.Named()`, `.Exposes()`, `.WithAlias()`, `.With()`, `.Add()`, `.ToRequest()`
- `WaevaRpcClientBase`: Shared `Call<T>()` with auto-deserialization and `WaevaRpcException` on errors

## Error Model

```csharp
WaevaRpcResponse {
    int Code;                    // HTTP-like status code
    string? Data;                // JSON result or error message
    byte[]? Content;             // Binary payload
    string? Id;                  // Request correlation ID
    Dictionary<string, string>? ExposedDependencies;  // For chaining
    WaevaRpcError? Error;            // Structured error (when Code != 200)
    bool IsSuccess;               // true if 200-299
}

WaevaRpcError {
    int Code;
    string Message;
    string? Details;             // Stack trace in Development only
    string? RequestId;
}
```

Clients throw `WaevaRpcException` with `WaevaRpcError` on non-2xx responses.

## Attributes

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[WaevaRpcController("name")]` | Class | Marks a class as an RPC controller |
| `[WaevaRpcMethod("name")]` | Method | Marks a method as remotely callable |
| `[WaevaRpcAuthorise]` | Method | Requires authentication (optional role) |
| `[WaevaRpcDataContract]` | Class | Optional discovery override (bare = force-expand / `Exclude = true` = force-opaque); default is signature inference via the controller-assembly boundary |
| `[WaevaRpcDocumentation("summary")]` | Class/Method/Param | XML-like doc for discovery |
| `[WaevaRpcExample("json")]` | Class | Example JSON for developer UI |

## Extension Points

1. **Custom Interceptor**: Implement `IWaevaRpcInterceptor`, register in DI
2. **Custom Transport**: Implement `IWaevaRpcClient` (client) or create middleware (server)
3. **Custom Authorisation**: Extend `WaevaRpcAuthoriseAttribute.OnAuthorization()`
4. **Discovery Enhancement**: Extend `WaevaRpcDiscoveryService.BuildDiscoveryInfo()`