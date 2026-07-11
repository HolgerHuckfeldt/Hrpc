// ==============================================================================
// 01 — Single Call (TypeScript/JS-Client)
// ==============================================================================
// Ein einzelner RPC-Aufruf. Gezeigt für REST und WebSocket (SignalR gibt es
// client-seitig nur in C#). WebSocket ist der empfohlene primäre Kanal (persistent,
// geringe Latenz); REST ist zustandslos und am einfachsten.
// ==============================================================================

import {
  HrpcRestClient,
  HrpcWebSocketClient,
  HrpcCall,
  type HrpcRequest,
} from "hrpc-client";

// minimaler Client-Typ (eigene DTOs oder aus generiertem Client)
type Customer = { id: number; name: string; email: string };

// --- REST — funktionale Form (callJson wirft HrpcError bei Nicht-2xx) --------
const rest = new HrpcRestClient("https://localhost:5001", { bearer: token });
const c1 = await rest.callJson<Customer>("Customer", "GetCustomerById", { id: 42 });

// --- REST — Fluent Builder ---------------------------------------------------
const req: HrpcRequest = HrpcCall.init("Customer", "GetCustomerById")
  .with({ id: 42 }) // Object = named params; Array = positional
  .toRequest();
const r = await rest.call(req);
if (!r.isSuccess) console.warn(r.error); // manuelle Fehlerprüfung bei call()

// --- WebSocket (persistent, primärer Kanal) ----------------------------------
const ws = new HrpcWebSocketClient("wss://localhost:5001/hrpcws", { bearer: token });
await ws.connect(); // explizit — oder lazy beim ersten call()
const c2 = await ws.callJson<Customer>(HrpcCall.init("Customer", "GetCustomerById").with({ id: 42 }).toRequest());
await ws.close(); // terminiert die Verbindung (dispose)

// --- Raw-Form (ohne Fluent Builder) -----------------------------------------
// stringData = JSON.stringify(HrpcParameter[]); data ist der JSON-token des Werts.
const raw: HrpcRequest = {
  controller: "Customer",
  method: "GetCustomerById",
  id: "Customer.GetCustomerById",
  stringData: JSON.stringify([{ parameterName: "id", num: 0, data: JSON.stringify(42) }]),
};
const r2 = await rest.call(raw);
const c3 = r2.data ? (JSON.parse(r2.data) as Customer) : null; // data ist JSON-String bei call()