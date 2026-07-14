# Trame — Best Practices

**How to set up, structure, and operate a Trame server well: secure configuration, binary and
stream handling, controller/method naming conventions, and how Trame sits next to conventional
REST.**

This is the engineering guide. For the security posture specifically see
[SECURITY_GUIDE.md](SECURITY_GUIDE.md); for the wire format [PROTOCOL.md](PROTOCOL.md); for
step-by-step setup [GETTING_STARTED.md](GETTING_STARTED.md); for the feature tour
[README.md](README.md).

---

## 1. Set up and operate securely

### 1.1 Authenticate upstream, enforce in Trame

Trame reads `HttpContext.User`; it does not run an identity provider. Put your auth scheme
(JWT bearer, cookies, mTLS terminated at the reverse proxy) **before** the Trame transport so
that `HttpContext.User` is populated when the invoker's `CheckAuthorisation` runs. Then flip
the default-deny toggle:

```csharp
builder.Services.AddTrame(new TrameOptions
{
    RequireAuthentication = true,
    RateLimitPermitLimit  = 20,
    MaximumBatchSize      = 16,
    MaxDependencyPathLength = 128,
    AllowRecursiveDescent = false,
    EnableDetailedErrors  = builder.Environment.IsDevelopment(),
});
```

Full posture matrix, transport gates, and the deployment checklist:
[SECURITY_GUIDE.md](SECURITY_GUIDE.md).

### 1.2 Two error channels — use the right one

Controllers signal failure two ways, and confusing them is the most common Trame mistake:

- **Business / domain errors → return `TrameResponse`** via the `TrameResults` factory:
  `TrameResults.NotFound("…")`, `TrameResults.BadRequest("…")`, `TrameResults.Error(code, message, details?)`,
  `TrameResults.Ok(obj)`. The invoker passes it through verbatim — your `code`, `data`, and
  `error` reach the client unchanged, and the message is **not** gated by `EnableDetailedErrors`.
- **Unexpected / internal failures → throw.** Any throw becomes a generic `500` with no message
  leak; the stack lands in `error.details` only in Development.

Do **not** throw `TrameException` to set a custom code — the server has no `catch(TrameException)`;
it becomes a generic `500`. Control the code via `TrameResults.Error(...)`.

Because business-error messages always reach the client, keep them free of sensitive context
(internal ids, connection strings, stack snippets). Treat the message text as public output.

### 1.3 Controller lifetime and DI

Controllers are resolved **per call** via `IServiceScopeFactory.CreateScope()` — the invoker is a
singleton, controllers are scoped. Inject scoped services through the constructor; do not store
per-request state in controller fields. `CancellationToken` is injected automatically into any
method that declares it — pass it through to downstream work so batches and long calls stay
cancellable.

### 1.4 Environment and the Developer UI

- Run **Production** for anything exposed: `ASPNETCORE_ENVIRONMENT = Production`. This turns off
  `EnableDetailedErrors` (no stack leaks).
- The Developer UI (`/Trame`, served by `MapTrame`) is a dev tool. **Do not ship it to
  untrusted clients** — omit it in production or put it behind auth. It is invaluable internally
  (browse the contract, build batches visually, keep many call tabs open, codegen, history,
  workspace snapshots) but it is not a production surface.

### 1.5 Host and proxy

Trame caps the framework layer (1 MB REST body, 1 MB WS message, cardinality caps). The **global**
limits are the host's responsibility:

```csharp
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxConcurrentConnections = 200;
    k.Limits.MaxConcurrentUpgradedConnections = 100;
    k.Limits.MaxRequestBodySize = 2_000_000;
});
```

Put a reverse proxy (nginx, Caddy, App Gateway) in front for TLS termination, header filtering,
and — importantly for WebSocket — **connection-rate limiting** (the WS transport is branch
middleware, so per-connection rate limiting belongs to the proxy, e.g. `limit_conn`). Configure
CORS with a named, scoped policy if browser clients call you cross-origin; avoid `*`.

---

## 2. Binary and streams

### 2.1 `byte[]` travels out of band

A method may take or return `byte[]`. Bytes never sit inside the JSON `data` field — they travel
out of band (`binaryData` on the request, `content` on the response):

| Transport | `binaryData` (request) | `content` (response) |
|-----------|------------------------|----------------------|
| REST (JSON)              | base64       | base64       |
| WebSocket (JSON text)    | base64       | base64       |
| SignalR (MessagePack)    | native `bin` | native `bin` |

**Practice.** Keep binary payloads small and infrequent through Trame — they are capped at the
1 MB body / message limit, and base64 over REST/WS inflates by ~33%. For large or frequent
binary (file upload, media, bulk export), run a **plain REST or WebSocket endpoint alongside
Trame** and let it stream bytes directly. Trame's value is the call graph and the typed contract;
it is not a bulk-transfer channel.

If you need `byte[]` native (no base64) and you control both ends, use the SignalR transport with
`UseMessagePack = true`.

### 2.2 `IAsyncEnumerable<T>` is materialized on the wire

A method returning `IAsyncEnumerable<T>` is consumed server-side into a `List<T>` and serialized
as a JSON array. That is **not** streaming on the wire — the client receives the whole array once
the enumeration completes. Two consequences:

- **Bound it.** `MaxResultElementCount` (default 10 000) caps the materialized list at the source.
  Raise it deliberately; do not disable it (`0`) for untrusted callers — an unbounded stream is a
  memory DoS.
- **For true streaming semantics** (chunked delivery, backpressure, long-running producers), use
  the WebSocket or SignalR transport's native streaming channel, or a side-by-side streaming
  endpoint. Do not hold a Trame batch open for a minutes-long producer — dependency chaining is
  for bounded request-time work.

> There is no on-the-wire streaming for REST in v1. WebSocket/SignalR are the streaming
> transports. See [ROADMAP.md](ROADMAP.md) for native streaming evolution.

---

## 3. Naming conventions — command-oriented, J2EE-style

Trame dispatches by `"{Controller}_{Method}"` — **name only, no parameter-based overload
resolution**. Two `[TrameMethod]`s on the same controller, or two `[TrameController]`s app-wide,
must have distinct names or `Register` throws at startup (no silent shadowing). Naming is
therefore not cosmetic; it is part of the dispatch contract.

### 3.1 Controller = noun, method = verb

Trame is **command-oriented**, not resource-oriented. Mirror that in the names:

- **Controller** = the domain noun / aggregate (`Customer`, `Order`, `Invoice`, `Loyalty`).
- **Method** = the action verb (`Create`, `GetById`, `Update`, `Delete`, `Search`, `Cancel`,
  `Approve`).

```csharp
[TrameController("Customer")]
public class CustomerController(CustomerService service)
{
    [TrameMethod("GetById")]   public Task<Customer?>  GetById(int id, CancellationToken ct) => service.Get(id, ct);
    [TrameMethod("Create")]    public Task<int>        Create(string name)                   => service.Add(name);
    [TrameMethod("Update")]    public Task             Update(Customer c)                     => service.Save(c);
    [TrameMethod("Delete")]    public Task             Delete(int id)                         => service.Remove(id);
}
```

This is the J2EE/EJB convention translated to code-first: the session-facade method set was always
`create` / `findByPrimaryKey` / `findBy<Criteria>` / `update` / `remove`, grouped on the entity.
Here the controller *is* the entity facade, and the `[TrameMethod]` names are the verbs.

### 3.2 CRUD baseline

Adopt a small, consistent verb set so the contract is predictable across controllers:

| Operation | Method name | Notes |
|-----------|-------------|-------|
| Create    | `Create`    | returns the new id / entity |
| Read one  | `GetById`   | by primary key |
| Read many | `Search` / `FindOpen` / `GetByCustomer` | finder-style, see §3.3 |
| Update    | `Update`    | full or patch — your domain decides |
| Delete    | `Delete`    | by id |

### 3.3 Finders — variant reads get distinct names, not overloads

Because Trame does not resolve overloads by signature, "get customer by X" variants are
**separate method names**, in the EJB `find*` tradition:

```csharp
[TrameMethod("GetById")]     public Task<Customer?>  GetById(int id)               => …;
[TrameMethod("GetByEmail")]  public Task<Customer?>  GetByEmail(string email)      => …;
[TrameMethod("FindOpen")]    public Task<List<Order>> FindOpen(int customerId)     => …;
[TrameMethod("Search")]      public Task<List<Customer>> Search(string term, int page, int size) => …;
```

If you need C#-style "overloads," model them with distinct names (`Add`, `AddRange`, `Create`,
`CreateBatch`). This is explicit on the wire and unambiguous in discovery.

### 3.4 Domain verbs beyond CRUD

The strength of command-orientation is that the real verbs surface. Name methods after the
business operation, not after HTTP:

```csharp
[TrameController("Order")]
public class OrderController(OrderService s)
{
    [TrameMethod("Place")]    public Task<int>            Place(int customerId, List<int> articleIds) => s.Place(customerId, articleIds);
    [TrameMethod("Cancel")]   public Task                 Cancel(int orderId, string reason)          => s.Cancel(orderId, reason);
    [TrameMethod("Approve")]  public Task                 Approve(int orderId)                        => s.Approve(orderId);
    [TrameMethod("Reopen")]   public Task                 Reopen(int orderId)                         => s.Reopen(orderId);
}
```

`Place`, `Cancel`, `Approve` are the language your domain uses — keep it. There is no need to bend
them into `POST /orders/{id}/cancel` resource-sub-path thinking; the method name *is* the
operation.

### 3.5 Versioning

There is no built-in API versioning in v1. Encode the version in the controller name
(`Customer.v1`, `Customer.v2`) so old and new contracts coexist on one server. Keep the method
names stable across versions; add new methods rather than reshaping old ones.

---

## 4. Interplay with REST

### 4.1 Two models, one host

Conventional REST is **resource-oriented**: endpoints are nouns, the HTTP verb carries the
operation, status is the HTTP status. Trame is **command-oriented**: the method name carries the
operation, everything is a `POST` to `/api/trame/json` (or `/json/multi`, or a WebSocket/SignalR
frame), and status lives in the body `code` (envelope-at-200 — see
[SECURITY_GUIDE.md §5](SECURITY_GUIDE.md)).

They coexist cleanly on one host: map your REST endpoints with `app.MapControllers()` / minimal
APIs as usual, and map Trame with `app.MapTrame()`. Use each for what it does well:

| Reach for… | when |
|------------|------|
| **Plain REST** | a single resource by id; cacheable `GET`s; proxy- and curl-friendly ops surfaces; webhook receivers; large binary uploads/downloads. |
| **Trame** | a screen or action that needs multiple dependent calls in one roundtrip (dependency chaining), command fan-out with per-call isolation, a typed contract shared across REST/WebSocket/SignalR, or a .NET-to-.NET binary channel (SignalR+MessagePack). |

### 4.2 When the Trame batch beats the REST loop

A client that needs `Order → Customer + Lines → Articles + Address` over plain REST pays six
serial roundtrips and owns the call graph (extract ids, order calls, dedupe). The same screen as
a Trame batch is one roundtrip with the dependencies declared; the server runs the topological
graph and binds `@alias` values itself. See [docs/stories/01-the-n-plus-one-screen.md](docs/stories/01-the-n-plus-one-screen.md).

The rule of thumb: **if there is a graph, use Trame; if there is a row, use REST.** A single
`GET /api/orders/{id}` with no dependencies is already one roundtrip — Trame adds nothing. A
screen with four dependent reads is where the batch earns its keep.

### 4.3 Status semantics differ — bridge deliberately

When you mix the two, remember clients experience two status regimes:

- **REST**: the HTTP status is the status (`200`, `404`, `422`, `500`).
- **Trame native**: HTTP is always `200`; the body `code` is the status. Per-method auth failure
  is HTTP 200 + `code:401`. Framework-level gates (discovery, batch cap, WS upgrade) return real
  HTTP 401/400 because they reject before the invoker.

If a single client talks to both, handle them separately: REST by HTTP status, Trame by body
`code`. `TrameClient` already does this for you (`TrameException` on a non-2xx body `code`).

### 4.4 Discovery as the Swagger alternative

`GET /api/trame/discovery` returns the full contract — controllers, methods, parameter types,
examples, documentation — generated from your code at runtime. For internal consumers this
replaces the Swagger/OpenAPI page; the Developer UI (`/Trame`) is the interactive browser over
it. For external consumers, gate discovery behind auth (it is an attack-surface oracle — see
[SECURITY_GUIDE.md §2](SECURITY_GUIDE.md)) and hand clients the contract through your normal docs
channel, or let authenticated clients fetch it.

### 4.5 Migrating incrementally

You do not have to pick one model for a whole app. A common path: keep existing REST resources
untouched, add Trame for the new screens that need batching or chaining, and let both run on the
same host. Clients migrate screen by screen. The JSON-RPC 2.0 compat adapter
(`EnableJsonRpcCompat = true`) lets an existing JSON-RPC client drive the Trame contract
unchanged, as an adoption lure before graduating to the native wire for `@alias` chaining and
binary. See [JSONRPC_COMPAT.md](JSONRPC_COMPAT.md).