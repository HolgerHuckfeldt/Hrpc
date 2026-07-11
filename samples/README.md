# WaevaRpc Samples

Sauber kommentierte Code-Beispiele als Entwickler-Referenz. Jede Datei ist in
sich geschlossen und zeigt genau ein Szenario — Server-seitig (C#) und Client-seitig
(C# bzw. TypeScript).

## Szenarien

| # | Szenario | Was es zeigt |
|---|----------|--------------|
| 01 | Single Call | Ein einzelner RPC-Aufruf (fluent + raw) |
| 02 | Batch Parallel | Mehrere unabhängige Aufrufe in einer Roundtrip — `ExecutionMode.Parallel` |
| 03 | Batch Serial | Mehrere Aufrufe nacheinander — `ExecutionMode.Serial` |
| 04 | Dependencies | Chaining: ein Aufruf gibt einen Wert weiter (`@alias`), der nächste nutzt ihn |

## Aufbau

```
samples/
  server/                     # Drop-in Beispiel-Server (Controller + In-Memory-Store)
    SampleServer.cs
  csharp/                     # C#-Client-Beispiele (WaevaRpcClient)
    01-single-call.cs
    02-batch-parallel.cs
    03-batch-serial.cs
    04-dependency-chain.cs
  typescript/                 # TypeScript/JS-Client-Beispiele (waeva-rpc-client)
    01-single-call.ts
    02-batch-parallel.ts
    03-batch-serial.ts
    04-dependency-chain.ts
```

## Voraussetzungen

- **Server**: `dotnet add package WaevaRpc.Server` (bringt alle Transporte transitiv),
  dann `builder.Services.AddWaevaRpc(...)` + `app.UseWaevaRpcTransports()` + `app.MapWaevaRpc()`.
  Siehe `server/SampleServer.cs` für die gezeigten Controller.
- **C#-Client**: `dotnet add package WaevaRpc.Client`.
- **TS-Client**: `npm install waeva-rpc-client` (lokales Paket: `clients/ts`).

## Kernkonzepte kurz

- **Vertrag = C#-Klassen** (`[WaevaRpcController]` / `[WaevaRpcMethod]`) — kein `.proto`, keine IDL.
- **Parameter** werden als `WaevaRpcParameter[]` (Name + JSON-`Data`) gesendet, serverseitig
  nach Name gebunden. `CancellationToken` injiziert der Server automatisch.
- **JsonPath ist ergebnisrelativ**: `$` ist der serialisierte Rückgabewert der Methode
  (z. B. ein `int` oder ein `Customer`-Objekt) — **kein** `$.data`-Envelope.
  `$.Id` → Eigenschaft, `$[0].Id` → erstes Listenelement.
- **`@alias`**: Ein downstream-Aufruf nutzt `@alias` als Parameterwert; der Server
  löst es gegen die `ExposedDependencies` vorheriger Aufrufe auf. Die `DependencyMapping`
  (`alias → JsonPath`) deklariert, was ein Aufruf bereitstellt.
- **Mode**: `Parallel` = `Task.WhenAll` (keine Aliase). `Serial` = sequenziell, löst
  `@alias` auf. **Achtung**: sobald irgendein Request ein `DependencyMapping` hat,
  schaltet der Server automatisch auf topologische Batch-Ausführung — der `Mode` wird
  dann ignoriert. Für reine Chaining-Beispiele empfehlen wir trotzdem `Serial`.
- **Fehler**: Business-/Domänenfehler → `WaevaRpcResults.NotFound(...)` etc. zurückgeben
  (Code + Message erreichen den Client). Unerwartete Exceptions → generisches 500
  (Message-Leak nur mit `EnableDetailedErrors`).

> Alle Beispiele sind Referenz-Snippets. Die Client-Base-URLs und die In-Memory-Daten
> im Server-Beispiel sind Platzhalter für eigene Setups.