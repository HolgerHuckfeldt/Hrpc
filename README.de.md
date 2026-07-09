# Hrpc (Deutsch)

**Hrpc** ist ein hochperformantes, Multi-Transport-RPC-Framework für .NET 8, das die Einfachheit von REST mit der Performance persistenter Verbindungen verbindet.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> English version: [README.md](README.md)

---

## Das REST-Problem für RPC

REST ist hervorragend für ressourcenorientierte APIs — CRUD-Operationen auf Substantiven wie `/customers/42` oder `/orders`. Aber wenn man *Aktionen* aufrufen will (RPC-Stil), zwingt REST einen in unpassende Muster:

- **Verb-Substantiv-Missmatch**: `POST /api/customer/42/add-address` ist nicht RESTful, aber genau das braucht man
- **N+1-Problem**: Kunde laden, dann Bestellungen, dann pro Bestellung die Artikel? Das sind 3 sequenzielle HTTP-Roundtrips — der Client wartet auf jede Antwort, bevor er die nächste Anfrage sendet
- **Kein Batching**: 10 Methoden in einem Aufruf? REST bietet keine Möglichkeit — 10 separate HTTP-Requests, 10 Verbindungen, 10× Overhead
- **Keine Abhängigkeitsauflösung**: Aufruf A liefert eine ID, die Aufruf B braucht? Der Client muss A's Antwort parsen, die ID extrahieren und an B übergeben — jedes Mal

GraphQL löst Teile davon durch Query-Komposition, bringt aber eigene Komplexität mit: Schema-first-Design, Resolver, ein separates Typsystem, und es läuft trotzdem über HTTP/1.1.

## Warum nicht gRPC?

gRPC ist die offensichtliche Wahl für RPC auf .NET — also warum etwas anderes bauen?

1. **gRPC war nicht gut unterstützt**, als Hrpc entstand. gRPC-Web (für Browser) brauchte einen Proxy, native Browser-Unterstützung fehlte, und die .NET-Tooling war noch unausgereift.
2. **Schema-first ist der falsche Default für .NET-Entwickler.** gRPC erfordert `.proto`-Dateien, einen Code-Generator und einen extra Build-Schritt. Man schreibt das Schema in einer DSL, generiert C#-Stubs und implementiert diese. In .NET *sind* die C#-Klassen bereits das Schema — warum eine zweite Definition in einer anderen Sprache pflegen?
3. **gRPC bindet an einen Transport.** HTTP/2 + Protobuf ist toll für Server-zu-Server, aber Browser brauchen gRPC-Web, und über WebSocket oder REST geht es nur mit separater Implementierung.

Hrpc ist **code-first**: Man schreibt C#-Klassen und Methoden, dekoriert sie mit Attributen, und das Framework erledigt den Rest. Keine `.proto`-Dateien, keine Code-Generierung, kein separates Schema. Discovery-Metadaten werden zur Laufzeit aus dem Code generiert.

## Wie vergleicht sich Hrpc mit JSON-RPC?

[JSON-RPC 2.0](https://www.jsonrpc.org/) ist der geistige Vorläufer von Hrpc — beide sind methodenorientiert, nicht ressourcenorientiert, und beide nutzen JSON. Also warum nicht einfach JSON-RPC nutzen?

| | JSON-RPC 2.0 | **Hrpc** |
|---|---|---|
| Methodenorientierte Aufrufe | ✅ | ✅ |
| Batch-Calls | ✅ (JSON-Array) | ✅ |
| Dependency-Chaining | ❌ Aufrufe sind unabhängig | ✅ `@alias`-Auflösung |
| Typ-Verträge | ❌ untypisierte `params` | ✅ `[HrpcDataContract]` |
| API-Discovery | ❌ (braucht OpenRPC) | ✅ eingebaut `/api/hrpc/discovery` |
| Dokumentation | ❌ (extern) | ✅ `[HrpcDocumentation]` + `[HrpcExample]` |
| Streaming | ❌ | ✅ `IAsyncEnumerable<T>` |
| Autorisierung | ❌ (Transport-Ebene) | ✅ `[HrpcAuthorise]` |
| Fehlermodell | ✅ einfach (`code` + `message`) | ✅ `HrpcError` mit Details + RequestId |
| .NET-Integration | ❌ (Bibliotheken existieren, aber kein Framework) | ✅ native ASP.NET-Core-Middleware |
| Transport-Definitionen | ❌ (Spec ist nur Format) | ✅ REST + WebSocket + SignalR definiert |
| Vorkompilierte Ausführung | ❌ (Reflection) | ✅ Expression Trees |

**Kurz gesagt**: JSON-RPC definiert ein *Wire-Format*, Hrpc ist ein *Framework*. JSON-RPC sagt, wie man das JSON formatiert, aber hilft nicht bei Methoden-Discovery, Abhängigkeitsauflösung, Streaming, Autorisierung oder .NET-DI-Integration. Hrpc wurde entwickelt, ohne JSON-RPC zu kennen, löst aber dasselbe Kernproblem (REST ist falsch für RPC) mit deutlich mehr Funktionalität.

## Warum Hrpc?

| | REST | gRPC | GraphQL | JSON-RPC | **Hrpc** |
|---|---|---|---|---|---|
| Für RPC gemacht | ❌ | ✅ | teils | ✅ | ✅ |
| Code-first (ohne IDL) | ✅ | ❌ (`.proto`) | ❌ (Schema) | ✅ | ✅ |
| Browser-nativ (ohne Proxy) | ✅ | ❌ (gRPC-Web) | ✅ | ✅ | ✅ |
| Multi-transport | ❌ | ❌ | ❌ | nur Format | ✅ REST + WebSocket + SignalR |
| Batch-Calls | ❌ | ❌ | ✅ | ✅ | ✅ |
| Dependency-Chaining | ❌ | ❌ | ✅ | ❌ | ✅ |
| Typ-Verträge | ❌ | ✅ | ✅ | ❌ | ✅ |
| Streaming | ❌ | ✅ | ✅ | ❌ | ✅ `IAsyncEnumerable<T>` |
| Interceptor-Pipeline | ❌ | ✅ | ❌ | ❌ | ✅ |
| API-Discovery | OpenAPI | ❌ | Introspection | ❌ | ✅ eingebaut |
| Developer-UI | Swagger | ❌ | GraphiQL | ❌ | ✅ |
| Rate-Limiting | manuell | ❌ | ❌ | ❌ | ✅ eingebaut |

Hrpc wurde von Grund auf für RPC entwickelt — nicht als REST-Wrapper, sondern als echtes RPC-Framework, das REST lediglich als einen von mehreren Transports nutzt:

1. **Methodenorientiert, nicht ressourcenorientiert**: `[HrpcController("Customer")]` + `[HrpcMethod("AddOrder")]` — kein URL-Routing, keine HTTP-Verben, kein DTO-Mapping
2. **Batch-Calls**: 10 RPC-Aufrufe in einem Roundtrip — der Server führt sie aus und liefert 10 Antworten
3. **Dependency-Chaining** (von GraphQL inspiriert): Ein Request exponiert Werte via `Exposes("$", "orderId")` (ergebnisrelativer JSON-Path), der nächste nutzt `WithAlias("@orderId")` — der Server löst Abhängigkeiten in einem Roundtrip
4. **Multi-Transport**: REST für Kompatibilität, WebSocket für niedrige Latenz, SignalR für Browser-Clients — gleiche API, verschiedene Wire-Protokolle
5. **Expression-Tree-Ausführung**: Methoden werden zur Registrierungszeit zu Delegates kompiliert — keine Reflection pro Aufruf

| | REST | gRPC | SignalR | **Hrpc** |
|---|---|---|---|---|
| Multi-Transport | nein | nein | nein | **ja** (REST + WebSocket + SignalR) |
| Batch-Calls | nein | nein | nein | **ja** |
| Dependency-Chaining | nein | nein | nein | **ja** |
| Streaming | nein | ja | ja | **ja** (`IAsyncEnumerable<T>`) |
| Interceptor-Pipeline | nein | ja | nein | **ja** |
| API-Discovery | OpenAPI | nein | nein | **ja** (eingebaut) |
| Developer-UI | Swagger | nein | nein | **ja** |
| Rate-Limiting | manuell | nein | nein | **ja** (eingebaut) |

## Performance

Gemessen mit BenchmarkDotNet auf .NET 8.0, Intel Core i7-12700H:

| Szenario | REST | Hrpc REST | Hrpc WebSocket | Beschleunigung |
|----------|------:|----------:|---------------:|--------:|
| Einzelner Call (GetCustomerById) | 606 µs | 627 µs | **267 µs** | **2,3x** |
| Batch 10x (parallel) | 4.906 µs | 974 µs | **693 µs** | **7,1x** |
| Dependency-Chain (3 Calls, 1 Roundtrip) | 206 ms | 205 ms | **204 ms** | ersetzt 3 Roundtrips |

> Vollständige Benchmarks: siehe `HrpcBench/BENCHMARK-RESULTS.md`

## Schnellstart

### Server

```csharp
using HrpcHub.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Hrpc mit allen Transportschichten registrieren
builder.Services.AddHrpc(new HrpcOptions
{
    UseSignalR = true,
    UseMessagePack = true,
    MaximumParallelInvocationsPerClient = 100
});

// Controller registrieren (automatisch via [HrpcController] erkannt)
builder.Services.AddSingleton<MyService>();

var app = builder.Build();
app.UseHrpc();              // Hrpc REST-Endpunkte registrieren
app.UseWebSockets();
app.UseHrpcWebSocket("/hrpcws");  // WebSocket-Transport
app.MapHub<HrpcHub.Hub.HrpcHub>("/Hrpc");  // SignalR-Transport
app.Run();
```

### Controller

```csharp
[HrpcController("Customer")]
public class CustomerHandler(CustomerService service)
{
    [HrpcMethod("GetById")]
    public async Task<Customer?> GetById(int id, CancellationToken ct)
        => await service.GetById(id, ct);

    [HrpcMethod("Create")]
    [HrpcAuthorise]  // Optional: erfordert Authentifizierung
    public async Task<int> Create(string name)
        => await service.Add(name);
}
```

### Client

```csharp
using HrpcClient.Hrpc;

// Transport wählen
var client = new HrpcRestJsonClient("https://localhost:5001/");
// oder: new HrpcWebSocketClient("https://localhost:5001/")
// oder: new HrpcSignalrClient("https://localhost:5001/")

// Einzelner Aufruf
var request = HrpcCall.Init("Customer", "GetById")
    .With(42)
    .ToRequest();
var response = await client.Call(request);

// Typisierter Aufruf
var customer = await client.Call<Customer>(request);

// Batch mit Dependency-Chaining
var batch = new HrpcMultiRequest
{
    Mode = ExecutionMode.Serial,
    Requests = new List<HrpcRequest>
    {
        HrpcCall.Init("Customer", "Create")
            .With("Alice").Named("step1")
            .Exposes("$", "newId")
            .ToRequest(),
        HrpcCall.Init("Customer", "GetById")
            .WithAlias("@newId", "0")
            .Named("step2")
            .ToRequest()
    }
};
var responses = await client.Call(batch);
```

## Transportschichten

| Transport | Endpunkt | Anwendungsfall |
|-----------|----------|---------------|
| **REST** | `POST /api/hrpc/json` | Zustandslose Aufrufe, Load-Balancing |
| **WebSocket** | `ws://host/hrpcws` | Persistente Verbindungen, niedrige Latenz, viele kleine Calls |
| **SignalR** | `/Hrpc` Hub | Browser-Clients, Auto-Reconnect, MessagePack |

## Features

- **Multi-Transport**: Gleiche API über REST, WebSocket und SignalR
- **Batch-Calls**: Mehrere RPC-Aufrufe in einem Roundtrip (parallel oder seriell)
- **Dependency-Chaining**: Aufrufe verketten mit `@alias`-Platzhaltern, serverseitig aufgelöst
- **Streaming**: `IAsyncEnumerable<T>`-Support, als JSON-Arrays serialisiert
- **Interceptor-Pipeline**: `IHrpcInterceptor` für Logging, Tracing, Caching, Validierung
- **Discovery/MEX**: `GET /api/hrpc/discovery` liefert vollständige API-Metadaten
- **Developer-UI**: Eingebaute Web-Oberfläche unter `/developer`
- **Rate-Limiting**: Eingebauter Fixed-Window-Rate-Limiter
- **Autorisierung**: `[HrpcAuthorise]`-Attribut mit rollenbasiertem Zugriff
- **Expression-Tree-Ausführung**: Vorkompilierte Methoden-Delegates für maximale Performance

## Projektstruktur

| Projekt | Beschreibung |
|---------|-------------|
| `HrpcCommon` | Geteilte Modelle, Attribute, Exceptions |
| `HrpcCore` | Invoker, Discovery, Dependency-Resolver, Interceptors |
| `HrpcHub` | SignalR-Transport + `AddHrpc()` / `UseHrpc()` |
| `HrpcRest` | REST-Transport (Minimal APIs + MVC-Controller) |
| `HrpcWebSocket` | WebSocket-Transport (RFC 6455, JSON-Text-Frames) |
| `HrpcClient` | Client-Bibliothek (REST, WebSocket, SignalR) |
| `HrpcDeveloperUi` | Eingebaute Developer-Web-UI |
| `Hrpc` | Beispielanwendung |
| `HrpcTests` | Unit- + Integrationstests |
| `HrpcBench` | Performance-Benchmarks |

## Installation (NuGet)

```xml
<!-- Server: alle Transports -->
<PackageReference Include="Hrpc.All" Version="1.0.0" />

<!-- Oder einzelne Transports -->
<PackageReference Include="Hrpc.Core" Version="1.0.0" />
<PackageReference Include="Hrpc.Hub" Version="1.0.0" />
<PackageReference Include="Hrpc.Rest" Version="1.0.0" />
<PackageReference Include="Hrpc.WebSocket" Version="1.0.0" />

<!-- Client -->
<PackageReference Include="Hrpc.Client" Version="1.0.0" />
```

## Anforderungen

- .NET 8.0+
- ASP.NET Core 8.0+

## Lizenz

MIT