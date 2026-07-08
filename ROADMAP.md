# Hrpc Roadmap

> Nach-v1, nach vorne gerichtet. Diese Datei listet Features, die bewusst *nicht* in
> den ersten öffentlichen Release gekommen sind — entweder weil sie den v1-Vertrag
> berühren würden, oder weil sie optional sind und das Code-first-Default-Modell nicht
> verändern dürfen. Stand: 2026-07-08.

---

## Motivation: Was Batching + Dependency-Auflösung in der Praxis bringen

Hrpcs Kernversprechen ist nicht „eine hübsche Client-API", sondern **N+1- und
Roundtrip-Kosten eliminieren, ohne dass der Server dafür Spezialmethoden baut.**
Ein beobachtetes Beispiel aus einer realen Anwendung:

> Eine Seite, die zum Laden ursprünglich **über 5 Sekunden** brauchte, fiel mit
> gezieltem Hrpc-Batching auf **rund 100 ms** — **ohne Server-seitige Änderung**,
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
heute protobuf — und fehlt in Hrpc bislang.

v1 löst Versionierung rein als **Konvention** (`[HrpcController("Customer.v1")]`,
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

Ein Roslyn-Source-Generator kann zur Build-Zeit **kein** `/api/hrpc/discovery`
abrufen (kein Netz, kein laufender Server). Deshalb zwei Stücke:

1. **Contract-Export (Server-Seite).** Ein MSBuild-Target oder CLI-Tool erzeugt
   das Discovery-JSON — das mit `/api/hrpc/discovery` bereits existiert — und
   schreibt es in eine Datei, die ins Repo committed wird (z. B. `contract.hrpc.json`).
2. **Source-Generator (Client-Seite).** Ein `IIncrementalGenerator` liest die
   committed Datei via `AdditionalFiles` und erzeugt pro Controller eine
   partielle Klasse `CustomerClient` mit stark getypteten Methoden
   (`Task<Customer?> GetById(int id, CancellationToken ct)`) plus einer
   `HrpcControllerVersion`-Konstante. Intern bauen die Stubs weiterhin auf
   `HrpcCall`/`IHrpcClient` auf — sie sind ein getypter Wrapper, kein zweites
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
  `HrpcRequest`, ein Controller, Server lehnt nicht-passende Versionen ab. Mehr
  Metadata, größere Protokolländerung. Nur wählen, wenn Routing-Key nicht reicht.

### Bewusst akzeptierte Trade-offs

- **Laufzeit-Typsicherheit geht vor Compile-Time dort, wo Generierung nicht
  eingesetzt wird.** Wer das Default-Modell nutzt, hat weiterhin keine
  Build-Time-Sicherung auf verketteten Werten (`@alias`) — ein Tippfehler knallt
  erst zur Laufzeit als `400`. Das ist ein bewusstes Opfer für die Flexibilität
  des Code-first-Modells; der Generator ist das Gegenstück für Teams, die
  Build-Time-Sicherung brauchen.
- **Zweites Verkaufsmodell.** Hrpcs Pitch bislang: „code-first, no IDL, no code
  generation." Das bleibt als Default wahr. Der Generator muss klar als *opt-in*
  positioniert werden, nicht als Ersatz — sonst widerspricht er der Kernposition.
- **Contract-Snapshot ist Drift-anfällig per se.** Daher der Drift-Check als
  Pflichtbestandteil, nicht als Nice-to-have.

### Offene Entwurfsfragen (bei Implementierung entscheiden)

- Konkrete Form der Contract-Datei (reines Discovery-JSON vs. eigene
  Contract-Schema-Datei mit Versions-Metadaten).
- Ob der Generator nur Stubs oder auch Request/Response-DTOs (`[HrpcDataContract]`)
  als C#-Typen erzeugt — und wie er mit Typen umgeht, die client-seitig schon
  existieren (Namenskollisionen).
- Ob `version` als Wire-Feld (Gate) oder nur als Generatorkonstante (Routing-Key)
  umgesetzt wird.
- NuGet-Paketierung: separater Generator-Analyzer vs. Bestandteil von `Hrpc.Client`.

### Beziehung zu v1

v1 bleibt unverändert. Die `Customer.v1`-Konvention aus den *Known Limitations*
ist die v1-Story; dieser Feature macht sie zur Build-Time-Sicherung. Keine
Protokolländerung am Bestand (außer im optionalen Gate-Pfad), keine
Inkompatibilität für bestehende Clients.

---

## Später (v1.x+, unsortiert)

- **Policy-basierte Autorisierung** für `[HrpcAuthorise]` via `IAuthorizationHandler`,
  damit `403` (authentifiziert, aber nicht erlaubt) von `401` (nicht
  authentifiziert) unterschieden werden kann — aktuell Roadmap im
  RELEASE-PLAN Phase 3.1.
- **Direkte/Fluent-Handler-Registrierung** ohne `[HrpcController]`/`[HrpcMethod]`
  — für Szenarien, die keine Attribut-Scan-Registrierung wollen.
- **Input-Validierung** der Parameter (DataAnnotations / FluentValidation) im
  Interceptor-Pipeline.
- **Echte REST-Streams** statt Materialisierung zu JSON-Array (aktuell Limitation;
  WebSocket/SignalR bieten Streaming-Semantik).