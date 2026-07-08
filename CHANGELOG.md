# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-07-02

### Background

Hrpc was created to solve a fundamental mismatch: REST is designed for resource-oriented APIs (CRUD on nouns), but real-world applications often need to call *actions* (RPC-style). REST forces awkward patterns like `POST /api/customer/42/add-address`, N+1 roundtrips for dependent calls, and no way to batch multiple calls.

**Why not gRPC?** gRPC was not well-supported when Hrpc was created (no native browser support, immature .NET tooling), and its schema-first approach (`.proto` files + code generation) conflicts with .NET's natural code-first workflow. In .NET, your C# classes already define the contract — maintaining a separate IDL is unnecessary overhead.

Hrpc takes inspiration from GraphQL (dependency resolution, batch queries) and gRPC (method-oriented calling) but stays within the .NET ecosystem as a **code-first** framework: no `.proto` files, no code generation, just attributes on C# classes.

### Added
- Multi-transport RPC framework: REST, WebSocket, SignalR
- Batch-call support: parallel and serial execution of multiple RPC calls in one roundtrip
- Dependency-chaining: `@alias` placeholders resolved server-side via JsonPath
- Topological sort with cycle detection for dependency-aware batch execution
- Streaming support: `IAsyncEnumerable<T>` and `Task<IAsyncEnumerable<T>>`
- Interceptor pipeline: `IHrpcInterceptor` with built-in `HrpcLoggingInterceptor`
- Discovery/MEX: `GET /api/hrpc/discovery` returns full API metadata
- Developer UI: built-in web interface at `/developer`
- Rate-limiting: configurable fixed-window rate limiter
- Authorization: `[HrpcAuthorise]` attribute with role-based access
- Unified error model: `HrpcError` with structured error details
- Expression-Tree invocation: pre-compiled method delegates for performance
- Fluent client API: `HrpcCall.Init().With().Exposes().WithAlias().ToRequest()`
- `HrpcDataContract`, `HrpcDocumentation`, `HrpcExample` attributes for API metadata
- gRPC comparison endpoint for benchmarks
- 67 unit and integration tests

### Performance
- Hrpc REST is 2.5x-16x faster than classic REST for single calls
- Hrpc Batch is 7x faster than REST parallel for 10 calls
- Hrpc WebSocket is 2.3x faster than REST for persistent small calls
- Hrpc Dependency-Chain replaces N+1 pattern: 1 roundtrip instead of 3
- Consistently 3-9x less memory allocation compared to REST

### Changed
- Consolidated all shared models (`HrpcRequest`, `HrpcResponse`, `HrpcMultiRequest`, `HrpcParameter`, `ExecutionMode`) into `HrpcCommon`
- Unified exception handling with `HrpcException` + `HrpcError`
- Migrated from `Newtonsoft.Json` to `System.Text.Json` as sole JSON serializer
- `HrpcCommon` target framework changed from `netstandard2.1` to `net8.0`
- `EnableDetailedErrors` now bound to `IHostEnvironment.IsDevelopment()`

### Removed
- Removed `HrpcGrpc` stub project (gRPC transport was not implemented)
- Removed `Newtonsoft.Json` dependency from `HrpcRest`
- Removed duplicate model definitions from `HrpcCore` and `HrpcClient`
- Removed duplicate `HrpcException` from `HrpcClient`

### Fixed
- `HrpcDiscoveryService`: `RegisterType()` was not called for non-generic `[HrpcDataContract]` return types and parameters, causing `KeyNotFoundException`
- `IAsyncEnumerable<T>` interface detection: now checks `GetInterfaces()` for compiler-generated state machine types