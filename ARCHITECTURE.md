# Hrpc Architecture

> German version: [ARCHITECTURE.de.md](ARCHITECTURE.de.md)

## Design Philosophy

Hrpc was born from a simple observation: **REST is designed for resources, not for RPC.**

When calling actions (not CRUD on nouns), REST forces awkward patterns:
- URL paths that pretend to be nouns: `/api/customer/42/add-address`
- N+1 roundtrips for dependent calls: client waits for A, then calls B, then C
- No batching: 10 method calls = 10 HTTP requests with 10× overhead

Hrpc takes inspiration from **GraphQL** (dependency resolution, batch queries) and **gRPC** (method-oriented calling, binary transport) but stays within the .NET/ASP.NET Core ecosystem.

### Why Not gRPC?

Two key reasons Hrpc exists instead of just using gRPC:

1. **gRPC was not well-supported** at creation time. Browser support required gRPC-Web proxies, and .NET gRPC tooling was still immature. Hrpc supports REST, WebSocket, and SignalR out of the box — browsers can use WebSocket or REST without proxies.

2. **Code-first, not schema-first.** gRPC requires `.proto` files — a separate IDL that generates C# stubs. This means maintaining a second source of truth: the `.proto` and the C# implementation. Hrpc uses attributes on plain C# classes: `[HrpcController]`, `[HrpcMethod]`, `[HrpcDataContract]`. The C# code *is* the contract. Discovery metadata is generated at runtime, not compile time.

The core design principles:
1. **Method-oriented**: Controllers and methods, not routes and verbs
2. **Batch by default**: Multiple calls in one roundtrip, parallel or serial
3. **Server-side dependency resolution**: `@alias` chaining inspired by GraphQL field selection
4. **Transport-agnostic**: The same `HrpcRequest` works over REST, WebSocket, or SignalR
5. **Zero-reflection invocation**: Expression Trees compile method calls at startup
6. **Code-first**: No IDL, no `.proto` files, no code generation — C# classes are the schema

## Overview

Hrpc is a protocol-agnostic RPC engine that sits between your business logic and multiple transport layers. The core engine (`HrpcCore`) resolves method calls via pre-compiled Expression Trees, while transports (REST, WebSocket, SignalR) handle the wire protocol.

```
┌──────────────────────────────────────────────────────────┐
│                      Client                                │
│  HrpcCall.Init("Ctrl", "Method").With(args).ToRequest()   │
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
│  │ HrpcRest │ │ HrpcWebSocket│ │ HrpcHub   │  Transports   │
│  │ Endpoint │ │  Middleware  │ │ (SignalR) │               │
│  └────┬─────┘ └──────┬──────┘ └─────┬────┘               │
│       └──────────────┼───────────────┘                     │
│                      ▼                                     │
│        ┌─────────────────────────────┐                    │
│        │      IHrpcCore (Invoker)      │  Core             │
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
│        │   [HrpcController] classes   │  User Code         │
│        │   with [HrpcMethod] methods  │                    │
│        └─────────────────────────────┘                    │
└────────────────────────────────────────────────────────────┘
```

## Request/Response Flow

### Single Call
1. Client creates `HrpcRequest` with controller name, method name, and JSON parameters
2. Transport sends the request (HTTP POST, WebSocket frame, or SignalR hub invocation)
3. `HrpcInvoker` looks up the controller and method in the invoke cache
4. Interceptor pipeline runs (logging, tracing, etc.)
5. Authorization is checked (`[HrpcAuthorise]`)
6. Parameters are deserialized from JSON and matched by name
7. Method is invoked via pre-compiled Expression Tree delegate
8. Result is serialized to JSON and wrapped in `HrpcResponse`
9. Response is sent back through the transport

### Batch Call (Multi-Request)
1. Client sends `HrpcMultiRequest` with multiple `HrpcRequest` items and an `ExecutionMode`
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

### HrpcInvoker (`HrpcCore`)
- Singleton service, thread-safe
- `ConcurrentDictionary` caches: controller types and compiled `InvokeInfo`
- `CompileInvocation()`: Expression Trees create `Func<object, object?[], object?>` per method
- `BuildParameters()`: Deserializes JSON parameters, matches by parameter name, injects `CancellationToken`
- `ExecuteMethod()`: Creates DI scope, resolves controller instance, invokes compiled delegate
- Handles sync, async (Task/Task<T>), void, and `IAsyncEnumerable<T>` return types

### HrpcDiscoveryService (`HrpcCore`)
- Generates `DiscoveryInfo` with all controllers, methods, parameters, and types
- Only includes types marked with `[HrpcDataContract]`
- Extracts `[HrpcDocumentation]` summaries and `[HrpcExample]` JSON examples
- Cached with invalidation on new registrations

### DependencyGraphBuilder (`HrpcCore`)
- Topological sort of requests based on `DependencyMapping` and `@alias` usage
- Groups independent requests into parallel batches (level-based execution)
- Cycle detection throws `InvalidOperationException` with involved request IDs

### Interceptor Pipeline (`HrpcCore`)
- `IHrpcInterceptor`: `InvokeAsync(request, next, ct)`
- Pipeline wraps the actual method execution in reverse order
- Built-in: `HrpcLoggingInterceptor` (traces call duration)
- Custom interceptors registered via DI: `services.AddSingleton<IHrpcInterceptor, MyInterceptor>()`

### Transports

| Transport | Project | Wire Protocol | Key Features |
|-----------|---------|---------------|--------------|
| REST | HrpcRest | HTTP/1.1 + JSON | Minimal APIs at `/api/hrpc/json`, `/api/hrpc/json/multi`, `/api/hrpc/discovery` |
| WebSocket | HrpcWebSocket | RFC 6455 + JSON text frames | Auto-detects single vs. multi-request, multi-frame support |
| SignalR | HrpcHub | WebSocket + MessagePack | Hub methods `DoWork()` / `DoWorkMany()`, auto-reconnect |

### Client Library (`HrpcClient`)
- `IHrpcClient` interface: `Call(HrpcRequest)`, `Call<T>(HrpcRequest)`, `Call(HrpcMultiRequest)`
- `HrpcRestJsonClient`: HTTP-based, connection pooling, `IDisposable`
- `HrpcWebSocketClient`: Persistent connection, `SemaphoreSlim` for thread safety, `IAsyncDisposable`
- `HrpcSignalrClient`: Auto-reconnect with exponential backoff, MessagePack protocol
- `HrpcCall`: Fluent builder with `.Named()`, `.Exposes()`, `.WithAlias()`, `.With()`, `.Add()`, `.ToRequest()`
- `HrpcClientBase`: Shared `Call<T>()` with auto-deserialization and `HrpcException` on errors

## Error Model

```csharp
HrpcResponse {
    int Code;                    // HTTP-like status code
    string? Data;                // JSON result or error message
    byte[]? Content;             // Binary payload
    string? Id;                  // Request correlation ID
    Dictionary<string, string>? ExposedDependencies;  // For chaining
    HrpcError? Error;            // Structured error (when Code != 200)
    bool IsSuccess;               // true if 200-299
}

HrpcError {
    int Code;
    string Message;
    string? Details;             // Stack trace in Development only
    string? RequestId;
}
```

Clients throw `HrpcException` with `HrpcError` on non-2xx responses.

## Attributes

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[HrpcController("name")]` | Class | Marks a class as an RPC controller |
| `[HrpcMethod("name")]` | Method | Marks a method as remotely callable |
| `[HrpcAuthorise]` | Method | Requires authentication (optional role) |
| `[HrpcDataContract]` | Class | Marks a type for discovery/metadata |
| `[HrpcDocumentation("summary")]` | Class/Method/Param | XML-like doc for discovery |
| `[HrpcExample("json")]` | Class | Example JSON for developer UI |

## Extension Points

1. **Custom Interceptor**: Implement `IHrpcInterceptor`, register in DI
2. **Custom Transport**: Implement `IHrpcClient` (client) or create middleware (server)
3. **Custom Authorisation**: Extend `HrpcAuthoriseAttribute.OnAuthorization()`
4. **Discovery Enhancement**: Extend `HrpcDiscoveryService.BuildDiscoveryInfo()`