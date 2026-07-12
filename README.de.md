# Trame (Deutsch)

**Trame** ist ein hochperformantes, Multi-Transport-RPC-Framework für .NET 8, das die Einfachheit von REST mit der Performance persistenter Verbindungen verbindet.

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

1. **gRPC war nicht gut unterstützt**, als Trame entstand. gRPC-Web (für Browser) brauchte einen Proxy, native Browser-Unterstützung fehlte, und die .NET-Tooling war noch unausgereift.
2. **Schema-first ist der falsche Default für .NET-Entwickler.** gRPC erfordert `.proto`-Dateien, einen Code-Generator und einen extra Build-Schritt. Man schreibt das Schema in einer DSL, generiert C#-Stubs und implementiert diese. In .NET *sind* die C#-Klassen bereits das Schema — warum eine zweite Definition in einer anderen Sprache pflegen?
3. **gRPC bindet an einen Transport.** HTTP/2 + Protobuf ist toll für Server-zu-Server, aber Browser brauchen gRPC-Web, und über WebSocket oder REST geht es nur mit separater Implementierung.

Trame ist **code-first**: Man schreibt C#-Klassen und Methoden, dekoriert sie mit Attributen, und das Framework erledigt den Rest. Keine `.proto`-Dateien, keine Code-Generierung, kein separates Schema. Discovery-Metadaten werden zur Laufzeit aus dem Code generiert.

## Wie vergleicht sich Trame mit JSON-RPC?

[JSON-RPC 2.0](https://www.jsonrpc.org/) ist der geistige Vorläufer von Trame — beide sind methodenorientiert, nicht ressourcenorientiert, und beide nutzen JSON. Also warum nicht einfach JSON-RPC nutzen?

| | JSON-RPC 2.0 | **Trame** |
|---|---|---|
| Methodenorientierte Aufrufe | ✅ | ✅ |
| Batch-Calls | ✅ (JSON-Array) | ✅ |
| Dependency-Chaining | ❌ Aufrufe sind unabhängig | ✅ `@alias`-Auflösung |
| Typ-Verträge | ❌ untypisierte `params` | ✅ Signatur-Inferenz (`[TrameDataContract]` optional) |
| API-Discovery | ❌ (braucht OpenRPC) | ✅ eingebaut `/api/trame/discovery` |
| Dokumentation | ❌ (extern) | ✅ `[TrameDocumentation]` + `[TrameExample]` |
| Streaming | ❌ | ✅ `IAsyncEnumerable<T>` |
| Autorisierung | ❌ (Transport-Ebene) | ✅ `[TrameAuthorise]` |
| Fehlermodell | ✅ einfach (`code` + `message`) | ✅ `TrameError` mit Details + RequestId |
| .NET-Integration | ❌ (Bibliotheken existieren, aber kein Framework) | ✅ native ASP.NET-Core-Middleware |
| Transport-Definitionen | ❌ (Spec ist nur Format) | ✅ REST + WebSocket + SignalR definiert |
| Vorkompilierte Ausführung | ❌ (Reflection) | ✅ Expression Trees |

**Kurz gesagt**: JSON-RPC definiert ein *Wire-Format*, Trame ist ein *Framework*. JSON-RPC sagt, wie man das JSON formatiert, aber hilft nicht bei Methoden-Discovery, Abhängigkeitsauflösung, Streaming, Autorisierung oder .NET-DI-Integration. Trame wurde entwickelt, ohne JSON-RPC zu kennen, löst aber dasselbe Kernproblem (REST ist falsch für RPC) mit deutlich mehr Funktionalität.

## Warum Trame?

| | REST | gRPC | GraphQL | JSON-RPC | **Trame** |
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

Trame wurde von Grund auf für RPC entwickelt — nicht als REST-Wrapper, sondern als echtes RPC-Framework, das REST lediglich als einen von mehreren Transports nutzt:

1. **Methodenorientiert, nicht ressourcenorientiert**: `[TrameController("Customer")]` + `[TrameMethod("AddOrder")]` — kein URL-Routing, keine HTTP-Verben, kein DTO-Mapping
2. **Batch-Calls**: 10 RPC-Aufrufe in einem Roundtrip — der Server führt sie aus und liefert 10 Antworten
3. **Dependency-Chaining** (von GraphQL inspiriert): Ein Request exponiert Werte via `Exposes("$", "orderId")` (ergebnisrelativer JSON-Path), der nächste nutzt `WithAlias("@orderId")` — der Server löst Abhängigkeiten in einem Roundtrip. Ein Wildcard-Pfad `Exposes("$[*].Id", "ids")` sammelt **alle** Treffer in ein Array und injiziert es als einen list-typisierten Parameter — `Search → GetByIds(@ids)` in einem Roundtrip, wie ein serverseitiges `Select`
4. **Multi-Transport**: REST für Kompatibilität, WebSocket für niedrige Latenz, SignalR für Browser-Clients — gleiche API, verschiedene Wire-Protokolle
5. **Expression-Tree-Ausführung**: Methoden werden zur Registrierungszeit zu Delegates kompiliert — keine Reflection pro Aufruf

| | REST | gRPC | SignalR | **Trame** |
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

| Szenario | REST | Trame REST | Trame WebSocket | Beschleunigung |
|----------|------:|----------:|---------------:|--------:|
| Einzelner Call (GetCustomerById) | 606 µs | 627 µs | **267 µs** | **2,3x** |
| Batch 10x (parallel) | 4.906 µs | 974 µs | **693 µs** | **7,1x** |
| Dependency-Chain (3 Calls, 1 Roundtrip) | 206 ms | 205 ms | **204 ms** | ersetzt 3 Roundtrips |

> Vollständige Benchmarks: siehe `TrameBench/BENCHMARK-RESULTS.md`

## Schnellstart

### Server

```csharp
using TrameHub.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Trame mit allen Transportschichten registrieren
builder.Services.AddTrame(new TrameOptions
{
    UseSignalR = true,
    UseMessagePack = true,
    MaximumParallelInvocationsPerClient = 100
});

// Controller registrieren (automatisch via [TrameController] erkannt)
builder.Services.AddSingleton<MyService>();

var app = builder.Build();
app.UseTrame();              // Trame REST-Endpunkte registrieren
app.UseWebSockets();
app.UseTrameWebSocket("/tramews");  // WebSocket-Transport
app.MapHub<TrameHub.Hub.TrameHub>("/tramehub");  // SignalR-Transport
app.Run();
```

### Controller

```csharp
[TrameController("Customer")]
public class CustomerHandler(CustomerService service)
{
    [TrameMethod("GetById")]
    public async Task<Customer?> GetById(int id, CancellationToken ct)
        => await service.GetById(id, ct);

    [TrameMethod("Create")]
    [TrameAuthorise]  // Optional: erfordert Authentifizierung
    public async Task<int> Create(string name)
        => await service.Add(name);
}
```

### Client

```csharp
using TrameClient.Trame;

// Transport wählen
var client = new TrameRestJsonClient("https://localhost:5001/");
// oder: new TrameWebSocketClient("https://localhost:5001/")
// oder: new TrameSignalrClient("https://localhost:5001/")

// Einzelner Aufruf
var request = TrameCall.Init("Customer", "GetById")
    .With(42)
    .ToRequest();
var response = await client.Call(request);

// Typisierter Aufruf
var customer = await client.Call<Customer>(request);

// Batch mit Dependency-Chaining
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
| **REST** | `POST /api/trame/json` | Zustandslose Aufrufe, Load-Balancing |
| **WebSocket** | `ws://host/tramews` | Persistente Verbindungen, niedrige Latenz, viele kleine Calls |
| **SignalR** | `/tramehub` Hub | Browser-Clients, Auto-Reconnect, MessagePack |

## Features

- **Multi-Transport**: Gleiche API über REST, WebSocket und SignalR
- **Batch-Calls**: Mehrere RPC-Aufrufe in einem Roundtrip (parallel oder seriell)
- **Dependency-Chaining**: Aufrufe verketten mit `@alias`-Platzhaltern, serverseitig aufgelöst; Wildcard-Pfade (`$[*].Id`) fächern eine Ergebnis-Menge in einen einzelnen list-typisierten Parameter auf
- **Streaming**: `IAsyncEnumerable<T>`-Support, als JSON-Arrays serialisiert
- **Interceptor-Pipeline**: `ITrameInterceptor` für Logging, Tracing, Caching, Validierung
- **Discovery/MEX**: `GET /api/trame/discovery` liefert vollständige API-Metadaten
- **Developer-UI**: Eingebaute Web-Oberfläche unter `/Trame`
- **Rate-Limiting**: Eingebauter Fixed-Window-Rate-Limiter
- **Autorisierung**: `[TrameAuthorise]`-Attribut mit rollenbasiertem Zugriff
- **Expression-Tree-Ausführung**: Vorkompilierte Methoden-Delegates für maximale Performance

## Projektstruktur

| Projekt | Beschreibung |
|---------|-------------|
| `TrameCommon` | Geteilte Modelle, Attribute, Exceptions |
| `TrameCore` | Invoker, Discovery, Dependency-Resolver, Interceptors |
| `TrameHub` | SignalR-Transport + `AddTrame()` / `UseTrame()` |
| `TrameRest` | REST-Transport (Minimal APIs + MVC-Controller) |
| `TrameWebSocket` | WebSocket-Transport (RFC 6455, JSON-Text-Frames) |
| `TrameClient` | Client-Bibliothek (REST, WebSocket, SignalR) |
| `TrameDeveloperUi` | Eingebaute Developer-Web-UI |
| `Trame` | Beispielanwendung |
| `TrameTests` | Unit- + Integrationstests |
| `TrameBench` | Performance-Benchmarks |

## Installation (NuGet)

```xml
<!-- Server: alle Transports (Hub + REST + WebSocket + DevUI) -->
<PackageReference Include="Trame.Server" Version="1.0.0" />

<!-- Oder einzelne Transports -->
<PackageReference Include="Trame.Core" Version="1.0.0" />
<PackageReference Include="Trame.Hub" Version="1.0.0" />
<PackageReference Include="Trame.Rest" Version="1.0.0" />
<PackageReference Include="Trame.WebSocket" Version="1.0.0" />

<!-- Client -->
<PackageReference Include="Trame.Client" Version="1.0.0" />
```

## Anforderungen

- .NET 8.0+
- ASP.NET Core 8.0+

## Lizenz

MIT