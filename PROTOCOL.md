# Hrpc Wire Protocol Specification

> Version: 1.0.0 · Status: Draft
>
> This specification defines the Hrpc wire protocol so that clients and servers
> can be implemented in any language (JavaScript/TypeScript, Python, Go, Rust, etc.).

## Overview

Hrpc is a method-oriented RPC protocol. A client sends a `HrpcRequest` containing
a controller name, method name, and JSON-encoded parameters. The server invokes the
matching method and returns a `HrpcResponse` with a status code and JSON-encoded result.

Hrpc supports three transports (REST, WebSocket, SignalR) but the request/response
format is identical across all of them.

---

## Message Types

### HrpcRequest

Single method invocation.

```json
{
  "controller": "Customer",
  "method": "GetById",
  "stringData": "[{\"ParameterName\":\"id\",\"Data\":\"42\"}]",
  "id": "Customer.GetById",
  "dependencyMapping": null,
  "binaryData": null
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `controller` | string | ✅ | Target controller name |
| `method` | string | ✅ | Target method name |
| `stringData` | string (JSON) | ✅ | JSON array of `HrpcParameter` objects |
| `id` | string | ✅ | Request identifier for correlation |
| `dependencyMapping` | object\|null | ❌ | Map of alias → JsonPath for dependency chaining |
| `binaryData` | base64\|null | ❌ | Optional binary payload |

### HrpcParameter

Method parameter within `stringData`.

```json
{
  "parameterName": "id",
  "data": "42"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `parameterName` | string | ✅ | Parameter name (matched server-side by name) |
| `data` | string | ✅ | JSON-encoded parameter value (string, number, object, array) |
| `num` | int | ❌ | Positional index (alternative to name) |

> `data` is always a **JSON string** that the server deserializes into the target type.
> Example: `"42"` (number as string), `"\"hello\""` (string with escaped quotes),
> `"{\"id\":1,\"name\":\"Alice\"}"` (object as string).

### HrpcResponse

Result of a method invocation.

```json
{
  "code": 200,
  "data": "{\"id\":42,\"name\":\"Alice\"}",
  "content": null,
  "id": "Customer.GetById",
  "exposedDependencies": null,
  "error": null
}
```

| Field | Type | Description |
|-------|------|-------------|
| `code` | int | HTTP-like status code (200, 400, 401, 404, 500, etc.) |
| `data` | string\|null | JSON-encoded result (or error message if code != 2xx) |
| `content` | base64\|null | Optional binary response |
| `id` | string\|null | Correlates with request ID |
| `exposedDependencies` | object\|null | Map of alias → value for dependency chaining |
| `error` | object\|null | Structured error (when code != 2xx) |
| `isSuccess` | bool | Derived: true if code is 200-299 (not serialized) |

### HrpcError

Structured error details, included in `HrpcResponse.error` when `code != 2xx`.

```json
{
  "code": 404,
  "message": "Customer '99' nicht gefunden.",
  "details": null,
  "requestId": "Customer.GetById"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `code` | int | HTTP-like status code |
| `message` | string | Human-readable error message |
| `details` | string\|null | Additional details (stack trace in development only) |
| `requestId` | string\|null | Correlates with request ID |

### HrpcMultiRequest

Batch of multiple requests in one roundtrip.

```json
{
  "requests": [
    { "controller": "Customer", "method": "Add", "stringData": "[...]", "id": "step1",
      "dependencyMapping": { "newId": "$.data" } },
    { "controller": "Customer", "method": "GetById", "stringData": "[{\"ParameterName\":\"id\",\"Data\":\"@newId\"}]", "id": "step2" }
  ],
  "mode": 1
}
```

| Field | Type | Description |
|-------|------|-------------|
| `requests` | array of HrpcRequest | Individual RPC calls |
| `mode` | int | 0 = Parallel, 1 = Serial |

> When `mode = 1` (Serial) and any request has `dependencyMapping`, the server
> performs topological sorting and executes in dependency-aware batches.

---

## Status Codes

| Code | Meaning |
|------|---------|
| 200 | OK – method executed successfully |
| 204 | No Content – void method completed |
| 400 | Bad Request – invalid parameters or JSON |
| 401 | Unauthorized – `[HrpcAuthorise]` check failed |
| 403 | Forbidden – role-based authorization failed |
| 404 | Not Found – controller or method not found |
| 405 | Method Not Allowed – authorization denied |
| 429 | Too Many Requests – rate limit exceeded |
| 500 | Internal Server Error – method threw exception |

---

## Dependency Chaining

### Concept

A request can **expose** values from its response using `dependencyMapping`:
- `dependencyMapping: { "alias": "$.data" }` → extracts `$.data` from response, stores as `alias`
- `dependencyMapping: { "orderId": "$.data.orderId" }` → extracts nested value

A subsequent request can **use** exposed values with `@alias` placeholders:
- `"stringData": "[{\"ParameterName\":\"id\",\"Data\":\"@orderId\"}]"`
- The server replaces `@orderId` with the actual value from the previous response

### Resolution Rules

1. **Serial mode**: Responses are processed in order. Each response's `exposedDependencies`
   are merged into a shared context. Subsequent requests can reference any prior alias.
2. **Parallel mode**: All requests execute simultaneously. Dependencies are ignored
   (they can't be resolved if the provider hasn't completed yet).
3. **Auto-detection**: If any request has `dependencyMapping`, the server switches to
   topological batch execution regardless of the specified mode.
4. **Cycle detection**: If a cycle is detected (A depends on B, B depends on A),
   all affected requests return 400 with error message listing involved request IDs.
5. **JsonPath**: Values are extracted using [JsonPath](https://goessner.net/articles/JsonPath/)
   expressions against the response `data` field (which is a JSON string, parsed first).

---

## Transports

### REST (HTTP/1.1 + JSON)

| Endpoint | Method | Body | Response |
|----------|--------|------|----------|
| `POST /api/hrpc/json` | POST | HrpcRequest | HrpcResponse |
| `POST /api/hrpc/json/multi` | POST | HrpcMultiRequest | array of HrpcResponse |
| `GET /api/hrpc/discovery` | GET | – | DiscoveryInfo |

**Content-Type**: `application/json`

**Example** (single call):
```
POST /api/hrpc/json HTTP/1.1
Content-Type: application/json

{
  "controller": "Customer",
  "method": "GetById",
  "stringData": "[{\"ParameterName\":\"id\",\"Data\":\"42\"}]",
  "id": "Customer.GetById"
}
```

**Response**:
```
HTTP/1.1 200 OK
Content-Type: application/json

{
  "code": 200,
  "data": "{\"id\":42,\"name\":\"Alice\"}",
  "id": "Customer.GetById"
}
```

### WebSocket (RFC 6455 + JSON text frames)

**URL**: `ws://host/hrpcws` or `wss://host/hrpcws`

**Protocol**: JSON text messages (one per request/response).

**Message types** (auto-detected by server):
- If JSON has `requests` and `mode` fields → `HrpcMultiRequest` → returns array of `HrpcResponse`
- Otherwise → `HrpcRequest` → returns single `HrpcResponse`

**Flow**:
1. Client connects to `ws://host/hrpcws`
2. Client sends JSON text frame (HrpcRequest or HrpcMultiRequest)
3. Server responds with JSON text frame (HrpcResponse or array)
4. Repeat (connection stays open)

### SignalR (WebSocket + MessagePack)

**Hub endpoint**: `/Hrpc`

**Hub methods**:
| Method | Parameters | Returns |
|--------|-----------|---------|
| `DoWork` | `HrpcRequest request` | `HrpcResponse?` |
| `DoWorkMany` | `HrpcMultiRequest request` | `IEnumerable<HrpcResponse>` |

**Protocol**: MessagePack binary (optional, can be JSON if configured).

---

## Discovery (MEX)

`GET /api/hrpc/discovery` returns `DiscoveryInfo`:

```json
{
  "controllers": [
    {
      "name": "Customer",
      "methods": [
        {
          "methodName": "GetById",
          "returnType": "Customer",
          "parameters": [
            {
              "parameterName": "id",
              "parameterType": "int"
            }
          ],
          "documentation": "Returns a customer by ID"
        }
      ]
    }
  ],
  "types": {
    "Hrpc.Model.Customer": {
      "typeName": "Hrpc.Model.Customer",
      "properties": [
        { "propertyName": "Id", "propertyType": "int" },
        { "propertyName": "Name", "propertyType": "string" }
      ],
      "example": { "id": 0, "name": "string" }
    }
  }
}
```

---

## TypeScript Client Example

```typescript
interface HrpcRequest {
  controller: string;
  method: string;
  stringData: string;
  id: string;
  dependencyMapping?: Record<string, string> | null;
}

interface HrpcResponse {
  code: number;
  data: string | null;
  id: string | null;
  error: { code: number; message: string; details?: string } | null;
  exposedDependencies?: Record<string, string> | null;
}

async function hrpcCall(
  url: string,
  controller: string,
  method: string,
  params: Record<string, any>,
  id?: string
): Promise<HrpcResponse> {
  const request: HrpcRequest = {
    controller,
    method,
    stringData: JSON.stringify(
      Object.entries(params).map(([name, value]) => ({
        parameterName: name,
        data: JSON.stringify(value),
      }))
    ),
    id: id ?? `${controller}.${method}`,
  };

  const resp = await fetch(`${url}/api/hrpc/json`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  return resp.json();
}

// Usage
const result = await hrpcCall("https://localhost:5001", "Customer", "GetById", { id: 42 });
const customer = JSON.parse(result.data!);
```

## Python Client Example

```python
import json
import requests
from typing import Any

def hrpc_call(url: str, controller: str, method: str, params: dict[str, Any], id: str = None) -> dict:
    request = {
        "controller": controller,
        "method": method,
        "stringData": json.dumps([
            {"parameterName": k, "data": json.dumps(v)}
            for k, v in params.items()
        ]),
        "id": id or f"{controller}.{method}",
    }
    response = requests.post(f"{url}/api/hrpc/json", json=request)
    return response.json()

# Usage
result = hrpc_call("https://localhost:5001", "Customer", "GetById", {"id": 42})
customer = json.loads(result["data"])
print(customer["name"])
```

---

## Design Principles for Cross-Platform Implementations

1. **Wire format is JSON** – no binary dependency for the protocol itself (MessagePack is optional for SignalR only)
2. **Parameter values are JSON strings** – `stringData` contains a JSON array of `{parameterName, data}` where `data` is itself a JSON string. This allows lazy deserialization on the server.
3. **Discovery enables code generation** – the `/api/hrpc/discovery` endpoint returns full type metadata, enabling auto-generated clients in any language.
4. **No schema language required** – unlike gRPC (`.proto`) or GraphQL (SDL), the contract is defined in code and discovered at runtime.