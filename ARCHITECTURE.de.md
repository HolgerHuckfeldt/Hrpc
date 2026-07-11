# WaevaRpc Architektur

> English version: [ARCHITECTURE.md](ARCHITECTURE.md)

## Design-Philosophie

WaevaRpc entstand aus einer einfachen Beobachtung: **REST ist für Ressourcen gemacht, nicht für RPC.**

Wenn man Aktionen aufruft (nicht CRUD auf Substantiven), zwingt REST in unpassende Muster:
- URL-Pfade, die Substantive simulieren: `/api/customer/42/add-address`
- N+1-Roundtrips für abhängige Aufrufe: Client wartet auf A, ruft dann B auf, dann C
- Kein Batching: 10 Methodenaufrufe = 10 HTTP-Requests mit 10× Overhead

WaevaRpc lässt sich von **GraphQL** (Abhängigkeitsauflösung, Batch-Queries) und **gRPC** (methodenorientierte Aufrufe, binärer Transport) inspirieren, bleibt aber im .NET/ASP.NET-Core-Ökosystem.

### Warum nicht gRPC?

Zwei wesentliche Gründe, warum WaevaRpc existiert statt einfach gRPC zu nutzen:

1. **gRPC war nicht gut unterstützt** zur Entstehungszeit. Browser-Unterstützung erforderte gRPC-Web-Proxys, und die .NET-gRPC-Tooling war noch unausgereift. WaevaRpc unterstützt REST, WebSocket und SignalR out-of-the-box — Browser können WebSocket oder REST nutzen, ohne Proxys.

2. **Code-first, nicht schema-first.** gRPC erfordert `.proto`-Dateien — eine separate IDL, die C#-Stubs generiert. Das bedeutet: eine zweite Source-of-Truth pflegen — die `.proto` und die C#-Implementierung. WaevaRpc nutzt Attribute auf normalen C#-Klassen: `[WaevaRpcController]`, `[WaevaRpcMethod]`, `[WaevaRpcDataContract]`. Der C#-Code *ist* der Vertrag. Discovery-Metadaten werden zur Laufzeit generiert, nicht zur Compile-Zeit.

Die Kern-Designprinzipien:
1. **Methodenorientiert**: Controller und Methoden, nicht Routen und Verben
2. **Batch als Standard**: Mehrere Aufrufe in einem Roundtrip, parallel oder seriell
3. **Serverseitige Abhängigkeitsauflösung**: `@alias`-Chaining inspiriert von GraphQL-Field-Selection
4. **Transport-agnostisch**: Derselbe `WaevaRpcRequest` funktioniert über REST, WebSocket oder SignalR
5. **Zero-Reflection-Ausführung**: Expression Trees kompilieren Methodenaufrufe beim Startup
6. **Code-first**: Keine IDL, keine `.proto`-Dateien, keine Code-Generierung — C#-Klassen sind das Schema

## Übersicht

WaevaRpc ist eine protokoll-agnostische RPC-Engine, die zwischen der Geschäftslogik und mehreren Transportschichten sitzt. Die Kern-Engine (`WaevaRpcCore`) löst Methodenaufrufe über vorkompilierte Expression Trees auf, während die Transports (REST, WebSocket, SignalR) das Wire-Protokoll handhaben.

## Anfrage/Antwort-Fluss

### Einzelner Aufruf
1. Der Client erstellt einen `WaevaRpcRequest` mit Controller-Name, Methoden-Name und JSON-Parametern
2. Der Transport sendet die Anfrage (HTTP POST, WebSocket-Frame oder SignalR-Hub-Aufruf)
3. `WaevaRpcInvoker` sucht Controller und Methode im Invoke-Cache
4. Interceptor-Pipeline läuft (Logging, Tracing, etc.)
5. Autorisierung wird geprüft (`[WaevaRpcAuthorise]`)
6. Parameter werden aus JSON deserialisiert und nach Name gematcht
7. Methode wird über vorkompilierten Expression-Tree-Delegate aufgerufen
8. Ergebnis wird als JSON serialisiert und in `WaevaRpcResponse` verpackt
9. Antwort wird über den Transport zurückgesendet

### Batch-Aufruf (Multi-Request)
1. Client sendet `WaevaRpcMultiRequest` mit mehreren `WaevaRpcRequest`-Einträgen und einem `ExecutionMode`
2. Bei vorhandenem `DependencyMapping` schaltet die Auto-Erkennung auf topologische Batch-Ausführung um
3. **Parallel**: Alle Requests parallel via `Task.WhenAll`
4. **Serial**: Requests nacheinander mit Dependency-Auflösung
5. **Dependency-Batch**: `DependencyGraphBuilder` erstellt Ausführungs-Batches:
   - Level 0: Alle Requests ohne Dependencies → parallel
   - Level 1: Requests, die nur von Level 0 abhängen → parallel
   - Level N: Requests, die von Level N-1 abhängen → parallel
6. Jedes Level läuft parallel, Levels nacheinander

### Dependency-Chaining
1. Request A deklariert `DependencyMapping: { "alias" → "$" }` (ergebnisrelativer JSON-Path; `$` ist das ganze Resultat, `$.Eigenschaft` eine Eigenschaft, `$[0].Id` ein Listenelement)
   - Nach der Ausführung extrahiert der Server Werte aus A's Response via JsonPath
   - Extrahierte Werte werden in `ExposedDependencies: { "alias" → value }` gespeichert
2. Request B verwendet `@alias` als Parameter-Platzhalter
   - Der Server ersetzt `@alias` durch den tatsächlichen Wert aus A's `ExposedDependencies`
3. Dies ermöglicht 3 abhängige Aufrufe in einem einzigen HTTP-Roundtrip

## Kernkomponenten

### WaevaRpcInvoker (`WaevaRpcCore`)
- Singleton-Service, thread-sicher
- `ConcurrentDictionary`-Caches: Controller-Typen und kompilierte `InvokeInfo`
- `CompileInvocation()`: Expression Trees erstellen `Func<object, object?[], object?>` pro Methode
- `BuildParameters()`: Deserialisiert JSON-Parameter, matcht nach Parameternamen, injiziert `CancellationToken`
- `ExecuteMethod()`: Erstellt DI-Scope, löst Controller-Instanz auf, ruft kompilierten Delegate auf
- Behandelt synchrone, asynchrone (Task/Task<T>), void und `IAsyncEnumerable<T>`-Rückgabetypen

### WaevaRpcDiscoveryService (`WaevaRpcCore`)
- Generiert `DiscoveryInfo` mit allen Controllern, Methoden, Parametern und Typen
- Nur Typen mit `[WaevaRpcDataContract]` werden einbezogen
- Extrahiert `[WaevaRpcDocumentation]`-Zusammenfassungen und `[WaevaRpcExample]`-JSON-Beispiele
- Gecacht mit Invalidierung bei neuen Registrierungen

### DependencyGraphBuilder (`WaevaRpcCore`)
- Topologische Sortierung der Requests basierend auf `DependencyMapping` und `@alias`-Verwendung
- Gruppiert unabhängige Requests in parallele Batches (Level-basiert)
- Zykluserkennung wirft `InvalidOperationException` mit beteiligten Request-IDs

### Interceptor-Pipeline (`WaevaRpcCore`)
- `IWaevaRpcInterceptor`: `InvokeAsync(request, next, ct)`
- Pipeline umschließt die Methodenausführung in umgekehrter Reihenfolge
- Eingebaut: `WaevaRpcLoggingInterceptor` (misst Aufrufdauer)
- Eigene Interceptors via DI registrieren: `services.AddSingleton<IWaevaRpcInterceptor, MyInterceptor>()`

### Transportschichten

| Transport | Projekt | Wire-Protokoll | Features |
|-----------|---------|---------------|----------|
| REST | WaevaRpcRest | HTTP/1.1 + JSON | Minimal APIs unter `/api/waeva/json`, `/api/waeva/json/multi`, `/api/waeva/discovery` |
| WebSocket | WaevaRpcWebSocket | RFC 6455 + JSON-Text-Frames | Erkennt Single vs. Multi-Request automatisch, Multi-Frame-Support |
| SignalR | WaevaRpcHub | WebSocket + MessagePack | Hub-Methoden `DoWork()` / `DoWorkMany()`, Auto-Reconnect |

### Client-Bibliothek (`WaevaRpcClient`)
- `IWaevaRpcClient`-Interface: `Call(WaevaRpcRequest)`, `Call<T>(WaevaRpcRequest)`, `Call(WaevaRpcMultiRequest)`
- `WaevaRpcRestJsonClient`: HTTP-basiert, Connection-Pooling, `IDisposable`
- `WaevaRpcWebSocketClient`: Persistente Verbindung, `SemaphoreSlim` für Thread-Safety, `IAsyncDisposable`
- `WaevaRpcSignalrClient`: Auto-Reconnect mit exponentiellem Backoff, MessagePack-Protokoll
- `WaevaRpcCall`: Fluent Builder mit `.Named()`, `.Exposes()`, `.WithAlias()`, `.With()`, `.Add()`, `.ToRequest()`

## Fehlermodell

```csharp
WaevaRpcResponse {
    int Code;                    // HTTP-ähnlicher Statuscode
    string? Data;                // JSON-Ergebnis oder Fehlermeldung
    byte[]? Content;             // Binärer Payload
    string? Id;                  // Request-Korrelations-ID
    Dictionary<string, string>? ExposedDependencies;  // Für Chaining
    WaevaRpcError? Error;            // Strukturierter Fehler (wenn Code != 200)
    bool IsSuccess;               // true bei 200-299
}

WaevaRpcError {
    int Code;
    string Message;
    string? Details;             // Stacktrace nur in Development
    string? RequestId;
}
```

Clients werfen `WaevaRpcException` mit `WaevaRpcError` bei non-2xx-Antworten.

### Fehler aus einem Controller zurückgeben

Zwei Wege — nicht austauschbar:

- **Business-/Domain-Fehler → `WaevaRpcResponse` zurückgeben** (empfohlen). Der Invoker
  reicht eine zurückgegebene `WaevaRpcResponse` unverändert durch
  (`WaevaRpcInvoker.ReturnResponse`: `if (result is WaevaRpcResponse) return it;`), sodass
  `Code` + `Data` + `Error` 1:1 beim Client ankommen. Fabrik `WaevaRpcResults`
  (`WaevaRpcCommon.Results`) belegt `Data` (Mensch-lesbare Message) **und** das
  strukturierte `WaevaRpcError` konsistent:
  ```csharp
  using WaevaRpcCommon.Results;
  [WaevaRpcMethod("GetById")]
  public WaevaRpcResponse GetById(int id)
  {
      var c = _repo.Find(id);
      if (c is null) return WaevaRpcResults.NotFound($"Customer '{id}' not found.");
      return WaevaRpcResults.Ok(c);
  }
  ```
  API: `Ok(object?|string|byte[])`, `NoContent()`, `Error(code, message, details?)`,
  Convenience `BadRequest`/`Unauthorized`/`NotFound`/`Conflict`/`InternalServerError`,
  RFC-7807-Überladung `Error(ProblemDetails)`. Die Message ist **nicht** an
  `EnableDetailedErrors` gekoppelt — sie kommt in jeder Umgebung beim Client an.
- **Unerwartetes/internes Versagen → werfen**. Jede Exception wird zu `500` mit
  **generischer** Message (kein Leak); der Stacktrace steht nur in `Error.Details`,
  wenn `EnableDetailedErrors` an ist (Development). Für Validierung/"not found" also
  falsch — der Client sähe nur das generische 500. `WaevaRpcException` zu werfen
  **propagiert keinen Code** (der Server hat kein `catch(WaevaRpcException)`); Code per
  `WaevaRpcResults.Error(...)` steuern.

## Attribute

| Attribut | Ziel | Zweck |
|-----------|--------|---------|
| `[WaevaRpcController("name")]` | Klasse | Markiert eine Klasse als RPC-Controller |
| `[WaevaRpcMethod("name")]` | Methode | Markiert eine Methode als remote aufrufbar |
| `[WaevaRpcAuthorise]` | Methode | Erfordert Authentifizierung (optionale Rolle) |
| `[WaevaRpcDataContract]` | Klasse | Markiert einen Typ für Discovery/Metadaten |
| `[WaevaRpcDocumentation("summary")]` | Klasse/Methode/Parameter | XML-ähnliche Doku für Discovery |
| `[WaevaRpcExample("json")]` | Klasse | Beispiel-JSON für Developer-UI |

## Erweiterungspunkte

1. **Custom Interceptor**: `IWaevaRpcInterceptor` implementieren, in DI registrieren
2. **Custom Transport**: `IWaevaRpcClient` (Client) oder Middleware (Server) implementieren
3. **Custom Autorisierung**: `WaevaRpcAuthoriseAttribute.OnAuthorization()` erweitern
4. **Discovery-Erweiterung**: `WaevaRpcDiscoveryService.BuildDiscoveryInfo()` erweitern