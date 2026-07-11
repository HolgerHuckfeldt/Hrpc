// ==============================================================================
// 02 — Batch Parallel (TypeScript/JS-Client)
// ==============================================================================
// Mehrere UNABHÄNGIGE Aufrufe in einer Roundtrip. Server führt sie per Task.WhenAll
// concurrent aus (ExecutionMode.Parallel). Ideal, um Latenz zu amortisieren, wenn
// Calls einander nicht brauchen.
//
// Wichtig: Parallel löst KEINE @alias-Abhängigkeiten auf — dafür Serial oder
// Dependency-Chaining siehe 03/04.
// ==============================================================================

import { TrameRestClient, TrameCall, ExecutionMode } from "trame-client";

const rest = new TrameRestClient("https://localhost:5001", { bearer: token });

// TrameCall.batch(requests, mode) — Default mode ist Serial; hier explizit Parallel.
const batch = TrameCall.batch(
  [
    // .named(id) setzt die Korrelations-Id (wichtig bei konkurrierenden Batches
    // über WebSocket, wo Responses an requests[0].id korrelieren).
    TrameCall.init("Customer", "GetAllCustomers").named("all").toRequest(),
    TrameCall.init("Customer", "GetCustomerById").with({ id: 1 }).named("c1").toRequest(),
    TrameCall.init("Customer", "GetCustomerById").with({ id: 2 }).named("c2").toRequest(),
  ],
  ExecutionMode.Parallel,
);

// callBatch(requests, mode) — Default mode ist hier Parallel; trotzdem explizit
// übergeben, wenn es auf die Semantik ankommt.
const responses = await rest.callBatch(batch.requests, batch.mode);

const [all, c1, c2] = responses; // Reihenfolge = Request-Reihenfolge
// all.data → "[{...},...]"  (JSON-String der Kundenliste)
// c1.data  → "{...}"        (ein Customer)