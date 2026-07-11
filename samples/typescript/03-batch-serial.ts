// ==============================================================================
// 03 — Batch Serial (TypeScript/JS-Client)
// ==============================================================================
// Mehrere Aufrufe nacheinander in einer Roundtrip (ExecutionMode.Serial). Server
// führt sie in Request-Reihenfolge sequenziell aus.
//
// Serial OHNE DependencyMapping/​@alias löst keine Aliase auf — es ist schlicht
// geordnete Ausführung. Wer Werte zwischen Calls weitergibt, braucht
// DependencyMapping (siehe 04).
// ==============================================================================

import { TrameRestClient, TrameCall, ExecutionMode } from "trame-client";

const rest = new TrameRestClient("https://localhost:5001", { bearer: token });

const batch = TrameCall.batch(
  [
    TrameCall.init("Customer", "GetCustomerById").with({ id: 1 }).named("a").toRequest(),
    TrameCall.init("Customer", "GetCustomerById").with({ id: 2 }).named("b").toRequest(),
  ],
  ExecutionMode.Serial,
);

const [a, b] = await rest.callBatch(batch.requests, batch.mode);

// Gotcha: sobald IRGENDEIN Request ein dependencyMapping hat, schaltet der Server
// automatisch auf topologische Batch-Ausführung und ignoriert mode. Für reine
// Serial-Semantik ohne Aliase einfach kein dependencyMapping setzen (wie hier).