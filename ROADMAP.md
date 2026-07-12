# Trame Roadmap

> Nach-v1, nach vorne gerichtet. Diese Datei listet Features, die bewusst *nicht* in
> den ersten öffentlichen Release gekommen sind — entweder weil sie den v1-Vertrag
> berühren würden, oder weil sie optional sind und das Code-first-Default-Modell nicht
> verändern dürfen. Stand: 2026-07-08.
>
> **Mit v1 geliefert:** Ein isomorpher JS/TypeScript-Client (REST + WebSocket) unter
> [`clients/ts/`](clients/ts/) (`npm i trame-client`). SignalR für JS/TS und
> Discovery→TS-Codegen bleiben offen (siehe „Später").

---

## Motivation: Was Batching + Dependency-Auflösung in der Praxis bringen

Trames Kernversprechen ist nicht „eine hübsche Client-API", sondern **N+1- und
Roundtrip-Kosten eliminieren, ohne dass der Server dafür Spezialmethoden baut.**
Ein beobachtetes Beispiel aus einer realen Anwendung:

> Eine Seite, die zum Laden ursprünglich **über 5 Sekunden** brauchte, fiel mit
> gezieltem Trame-Batching auf **rund 100 ms** — **ohne Server-seitige Änderung**,
> also ohne eine extra Methode nur für genau diesen Fall. Die Liste der Kunden,
> das Prefetchen der ersten beiden und die Bestellpositionen des ersten Kunden
> laufen in einem Roundtrip, so parallel wie die Abhängigkeiten es zulassen.

Das ist das Potenzial, das die Architektur trägt: der Client orchestriert, der
Server löst auf und parallelisiert. Die unten dokumentierten Features sind dazu
da, dieses Modell bei Skalierung *sicherer* zu machen — nicht, es zu ersetzen.

---

## v1.1 — Versionierung & Build-Time-Vertrag

### Problem

Versionierung ist das einzige Problem, das **jede RPC-Generation wieder eingeholt**
hat (CORBA → COM → SOAP → gRPC). Und die einzige Maßnahme, die verlässlich greift,
ist **Build-Time-Stub-Erzeugung**: ein Interface-Drift, der beim Kompilieren
knallt, statt erst im Produktionstraffic. Das war damals `wsdl.exe`/`svcutil`,
heute protobuf — und fehlt in Trame bislang.

v1 löst Versionierung rein als **Konvention** (`[TrameController("Customer.v1")]`,
dotted Namespace, siehe README *Known Limitations*). Das erlaubt Koexistenz von
v1/v2, bietet aber keine Build-Time-Sicherung: ein Client, der gegen eine alte
Form compiliert wurde, merkt einen Server-seitigen Interface-Bruch erst zur
Laufzeit.

### Ziel

Ein **optionales, opt-in** zweites Modell neben dem Code-first-Default:

- **Default bleibt:** Runtime-Discovery, keine Code-Generierung, kein IDL — wie in v1.
- **Opt-in:** Ein Source-Generator erzeugt aus einem committed Contract-Snapshot
  getypte Client-Stubs + Versionskonstanten. Interface-Bruch → Build fehlschlägt.

### Architektur (zweistückig — wie wsdl.exe, nur .NET-nativ)

Ein Roslyn-Source-Generator kann zur Build-Zeit **kein** `/api/trame/discovery`
abrufen (kein Netz, kein laufender Server). Deshalb zwei Stücke:

1. **Contract-Export (Server-Seite).** Ein MSBuild-Target oder CLI-Tool erzeugt
   das Discovery-JSON — das mit `/api/trame/discovery` bereits existiert — und
   schreibt es in eine Datei, die ins Repo committed wird (z. B. `contract.trame.json`).
2. **Source-Generator (Client-Seite).** Ein `IIncrementalGenerator` liest die
   committed Datei via `AdditionalFiles` und erzeugt pro Controller eine
   partielle Klasse `CustomerClient` mit stark getypteten Methoden
   (`Task<Customer?> GetById(int id, CancellationToken ct)`) plus einer
   `TrameControllerVersion`-Konstante. Intern bauen die Stubs weiterhin auf
   `TrameCall`/`ITrameClient` auf — sie sind ein getypter Wrapper, kein zweites
   Protokoll.
3. **Drift-Check (Pflichtbestandteil).** Ein MSBuild-Target auf dem
   Server-Projekt regeneriert das Discovery zur Build-Zeit und lässt den Build
   **fehlschlagen**, wenn das committed JSON abweicht. *Ohne diesen Check ist es
   genau die wsdl-Falle:* jemand ändert den Server, vergisst zu regenerieren,
   der Client-Build ist trotzdem grün. Der Drift-Check ist der Teil, der
   `wsdl.exe` damals besser gemacht hätte.

### Versionierungsmodell: Routing-Key, nicht Compatibility-Gate

- **Routing-Key (empfohlen):** `Customer.v1` und `Customer.v2` sind zwei Einträge
  im Controller-Dict, beide registriert; der Request wählt über das `controller`-Feld.
  Baut auf Vorhandenem auf, kein neues Wire-Feld nötig. Der Generator brennt die
  Version als Konstante in den Stub; der Server kann bei Mismatch hart
  `409`/`400` „Version mismatch" returnen — explizit statt stillschweigend.
- **Compatibility-Gate (alternative):** ein typisiertes `version`-Feld im
  `TrameRequest`, ein Controller, Server lehnt nicht-passende Versionen ab. Mehr
  Metadata, größere Protokolländerung. Nur wählen, wenn Routing-Key nicht reicht.

### Bewusst akzeptierte Trade-offs

- **Laufzeit-Typsicherheit geht vor Compile-Time dort, wo Generierung nicht
  eingesetzt wird.** Wer das Default-Modell nutzt, hat weiterhin keine
  Build-Time-Sicherung auf verketteten Werten (`@alias`) — ein Tippfehler knallt
  erst zur Laufzeit als `400`. Das ist ein bewusstes Opfer für die Flexibilität
  des Code-first-Modells; der Generator ist das Gegenstück für Teams, die
  Build-Time-Sicherung brauchen.
- **Zweites Verkaufsmodell.** Trames Pitch bislang: „code-first, no IDL, no code
  generation." Das bleibt als Default wahr. Der Generator muss klar als *opt-in*
  positioniert werden, nicht als Ersatz — sonst widerspricht er der Kernposition.
- **Contract-Snapshot ist Drift-anfällig per se.** Daher der Drift-Check als
  Pflichtbestandteil, nicht als Nice-to-have.

### Offene Entwurfsfragen (bei Implementierung entscheiden)

- Konkrete Form der Contract-Datei (reines Discovery-JSON vs. eigene
  Contract-Schema-Datei mit Versions-Metadaten).
- Ob der Generator nur Stubs oder auch Request/Response-DTOs (`[TrameDataContract]`)
  als C#-Typen erzeugt — und wie er mit Typen umgeht, die client-seitig schon
  existieren (Namenskollisionen).
- Ob `version` als Wire-Feld (Gate) oder nur als Generatorkonstante (Routing-Key)
  umgesetzt wird.
- NuGet-Paketierung: separater Generator-Analyzer vs. Bestandteil von `Trame.Client`.

### Beziehung zu v1

v1 bleibt unverändert. Die `Customer.v1`-Konvention aus den *Known Limitations*
ist die v1-Story; dieser Feature macht sie zur Build-Time-Sicherung. Keine
Protokolländerung am Bestand (außer im optionalen Gate-Pfad), keine
Inkompatibilität für bestehende Clients.

### Proof-of-Concept: `spikes/LinqProvider/` (Stand 2026-07-08)

Ein Expression-Tree-getypter RPC-Proxy-Spike validiert die Machbarkeit des
getypteten Client-Modells end-to-end gegen die Sample-App:

- `client.Build((ICustomerService c) => c.GetCustomerById(id))` → korrekter
  `TrameRequest` aus dem `MethodCallExpression` (Controller/Methode aus
  Vertrags-Attributen, Parameter aus der Signatur).
- **Typsichere Dep-Verdrahtung** über `Dep<T>`-Marker + `Arg<T>`-Wrapper mit
  impliziten Konvertierungen: ein `Dep<string>` an einer `Arg<int>`-Stelle ist
  ein Compile-Fehler, kein Laufzeit-500.
- `Expose(x => x.Name)` → ergebnisrelativer JsonPath `$.Name`.
- `ContractGenerator` erzeugt C#-Verträge aus `discovery.json` (Modell „c").

**Fazit: technisch gangbar, aber nicht in v1 ausgebaut.** Der Spike hat die
Daseinsberechtigung indirekt schon bewiesen — die getypte Verdrahtung hat die
drei Server-Bugs im Dependency-Chaining (`$.data`-Konvention, typgetreue
`@alias`-Substitution, nicht auflösender topologischer Batch-Pfad) überhaupt
erst sichtbar gemacht, weil sie einen numerischen Chain end-to-end ausführt.
Ehrlich eingeordnet ist der ROI aber begrenzt: der untypisierte
`TrameCall`-Builder funktioniert, Methoden-/Parameterfehler fallen in der
Entwicklung ohnehin sofort zur Laufzeit auf, und der größte Gewinn (typisierte
Dep-Verdrahtung) deckt den engsten Fall (Batch-Chains). Der Codegen +
`Arg<T>`-Tax rechtfertigt sich nur als opt-in.

**Aus dem Spike zu lösende Punkte für ein v1.x:**

- **Kein `IQueryable` nötig.** Der Spike ist ein getypter Proxy, kein Query-
  Provider — das IQueryable-Monad-Problem entfällt. Beim Entwurf darauf achten,
  keine unnötige Komplexität aus dem LINQ-Vorbild zu übernehmen.
- **Alias-Lexik ist server-seitig restriktiv.** `DependencyGraphBuilder.ExtractAliases`
  bricht an `.`/`#` u. ä. (nur `[A-Za-z0-9_]`). Der Spike sanitisiert
  generierte Aliase client-seitig; sauberer wäre, den Server freier extrahieren
  zu lassen — als Server-Change vorm Generator-Release prüfen.
- **`Arg<T>` nur wo nötig.** Der Spike wickelt jeden Parameter in `Arg<T>`; ein
  Produkt würde das nur an Dep-empfangenden Positionen tun, um die API nicht
  zu überladen.
- **Konstanten-Faltung + Caching** der Argument-Evaluation (`Expression.Lambda(arg).Compile().DynamicInvoke()`
  pro Call ist für heiße Loops zu teuer).
- **Durchreichung** von Cancellation, `IAsyncEnumerable`, `byte[]` — der
  untypisierte `ITrameClient` kann das schon; die getypte Schicht muss es spiegeln.

Details und vollständige Entscheidung im Spike unter
[`spikes/LinqProvider/README.md`](spikes/LinqProvider/README.md). Der Spike ist
bewusst *nicht* Teil von `Trame.sln` — er läuft isoliert:
`dotnet test spikes/LinqProvider/Trame.Spike.LinqProvider.csproj`.

---

## Binary-Transfer (v1.x+)

### Status quo (v1)

`byte[]`-Parameter (`TrameRequest.binaryData`) und `byte[]`-Rückgaben
(`TrameResponse.content`) sind Out-of-band zum JSON-`data`-Feld geführt — sie
konkurrieren nicht mit strukturierten Argumenten und werden nicht doppelt in
`data` kodiert. Die Wire-Kodierung ist transportabhängig:

- **REST und WebSocket (JSON):** Base64-in-JSON (~33 % Overhead), begrenzt
  durch die Message-Size-Caps (REST 1 MB Body, WebSocket 1 MB/Message,
  hardcodiert). WebSocket nimmt bewusst nur Text-Frames an — native
  Binary-Frames werden nicht akzeptiert.
- **SignalR (MessagePack):** native `bin`-Kodierung, kein Base64.

Offene Lücken, die in v1 bewusst nicht gelöst sind (siehe README *Known
Limitations* — Binary):

1. **C#-Fluent-Builder ohne `WithBinary`** — Binary-Upload aus C# nur per
   `request.BinaryData = …`. Helper fehlt (TS hat `withBinary`).
2. **First-Match-only für `byte[]`-Parameter** — eine Methode mit mehreren
   `byte[]`-Parametern bekommt die Payload nur im ersten.
3. **Kein Streaming** — `byte[]`-Responses puffern in `content`;
   `TrameResponse.ContentStream` ist am Modell deklariert, aber von keinem
   Transport angebunden.
4. **Kein Multipart/Chunking** für große REST-Uploads (nur Base64-in-JSON,
   1 MB-Cap).

### Ziel (v1.x+)

Binary-Transfer als gleichberechtigten, nicht base64-bepackten Pfad ausbauen —
ohne das JSON-Text-Wire für REST/WebSocket aufzugeben (Sprachneutralität bleibt
das Kriterium).

- **WebSocket native Binary-Frames** optional annehmen (Auto-Detection:
  Text-Frame = JSON-Request wie heute; Binary-Frame = rohe Payload für eine
  `byte[]`-Methode plus dünner Header für Controller/Methode/Id). Der
  Base64-Weg bleibt kompatibel.
- **`ContentStream` anbinden** — große `byte[]`-Responses streamen statt
  puffern (REST: chunked transfer; WebSocket/SignalR: sequentielle Frames).
- **`WithBinary` im C#-Builder** nachziehen, symmetrisch zum TS-Client.
- **Namens-Bindung für `byte[]`-Parameter** statt First-Match-only, sobald ein
  zweites Binary-Feld pro Request nötig ist — oder mehrere benannte
  Binary-Slots in `TrameRequest`.
- **Konfigurierbare Message-Size-Caps** über `TrameOptions` (WebSocket heute
  hardcodiert 1 MB).

### Bewusst akzeptierter Trade-off

Binary ist in v1 eine **Zweitklass-Bürger-Warnung wert**, kein Verkaufsargument:
wer große oder frequente Binary-Ströme braucht, lässt sie heute besser über
einen plain-REST- oder WebSocket-Endpunkt neben Trame laufen. Das RPC-Modell
trägt Befehls- und Chaining-Last, nicht Bulk-Transfer. Der v1.x+-Plan hebt
Binary auf dieses Niveau, ohne das JSON-Wire zu brechen.

### Offene Entwurfsfragen (bei Implementierung entscheiden)

- WebSocket Binary-Frame-Header-Format (minimal JSON-Präfix vs. eigener
  Frame-Sub-Typ) und Korrelation mit `id`.
- Ob `ContentStream` pro Transport oder generisch über einen Transport-Adapter
  läuft.
- Ob mehrere `byte[]`-Parameter über mehrere benannte Slots in `TrameRequest`
  oder über ein Binary-Multipart-Frame gelöst werden.

---

## Später (v1.x+, unsortiert)

- **SignalR-Client für JS/TS.** Der `clients/ts/`-Client (v1) deckt REST +
  WebSocket ab. Ein SignalR-Transport (`/Trame`-Hub, MessagePack) für den
  Browser/Node folgt als v1.1 — bewusst nicht in v1, um die schwere
  `@microsoft/signalr`-Abhängigkeit + MessagePack aus dem Default-Build
  herauszuhalten. REST + WebSocket decken Browser-RPC bereits ab.
- **Discovery→TypeScript-Codegen.** `/api/trame/discovery` liefert vollständige
  Typ-Metadaten. Ein Generator, der daraus getypte Client-Stubs (statt der
  heute rein funktionalen API) erzeugt, ist das JS-Äquivalent des oben
  beschriebenen .NET-Source-Generators und teilt dessen Drift-Check-Anforderung.
- **Policy-basierte Autorisierung** für `[TrameAuthorise]` via `IAuthorizationHandler`,
  damit `403` (authentifiziert, aber nicht erlaubt) von `401` (nicht
  authentifiziert) unterschieden werden kann — aktuell Roadmap im
  RELEASE-PLAN Phase 3.1.
- **Direkte/Fluent-Handler-Registrierung** ohne `[TrameController]`/`[TrameMethod]`
  — für Szenarien, die keine Attribut-Scan-Registrierung wollen.
- **Input-Validierung** der Parameter (DataAnnotations / FluentValidation) im
  Interceptor-Pipeline.
- **Echte REST-Streams** statt Materialisierung zu JSON-Array (aktuell Limitation;
  WebSocket/SignalR bieten Streaming-Semantik).