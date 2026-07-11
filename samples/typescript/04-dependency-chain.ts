// ==============================================================================
// 04 — Dependency Chaining (TypeScript/JS-Client)
// ==============================================================================
// Mehrere Aufrufe in EINER Roundtrip, wobei spätere Aufrufe Werte aus früheren
// nutzen — ohne Client-seitiges Zusammenfügen.
//
// Mechanik:
//   • Request A deklariert dependencyMapping: { alias: "$.JsonPath" }.
//     JsonPath ist ergebnisrelativ: "$" = gesamte serialisierte Rückgabe;
//     "$.Id" = Eigenschaft; "$[0].Id" = erstes Listenelement. KEIN "$.data"-Envelope.
//   • Server extrahiert den Wert → ExposedDependencies.
//   • Request B nutzt "@alias" als Parameterwert (data-String mit @-Präfix).
//     Server ersetzt es vor der Ausführung.
//   • mode = Serial (sobald ein dependencyMapping existiert, schaltet der Server
//     ohnehin auf topologische Batch-Ausführung — mode wird dann ignoriert).
// ==============================================================================

import { HrpcRestClient, HrpcCall, ExecutionMode, type HrpcRequest } from "hrpc-client";

const rest = new HrpcRestClient("https://localhost:5001", { bearer: token });

// -----------------------------------------------------------------------------
// Variante A — Fluent Builder (einfach, 2-Step, Ein-Parameter-@alias)
// -----------------------------------------------------------------------------
// AddCustomer → liefert neue Id (int) → weiter als @newId an GetCustomerById.
const batch = HrpcCall.batch(
  [
    HrpcCall.init("Customer", "AddCustomer")
      .named("step1")
      .with({ name: "Alice", email: "alice@x.com" })
      .exposes("$", "newId") // ganzer int-Rückgabewert → Alias "newId"
      .toRequest(),

    HrpcCall.init("Customer", "GetCustomerById")
      .named("step2")
      .withAlias("@newId") // data: "@newId", parameterName: "newId"
      .toRequest(),
    // Hinweis: withAlias setzt parameterName auf den Alias-Namen ("newId").
    // GetCustomerById hat nur EINEN echten Parameter ("id") → Server bindet
    // positional (num=0). Bei MEHRPARAMETRIGEN Methoden mit @alias siehe
    // Variante B (raw, parameterName = echter Parametername).
  ],
  ExecutionMode.Serial,
);

const [first, second] = await rest.callBatch(batch.requests, batch.mode);
// first.data  = neue Id (number, JSON-String)
// second.data = der angelegte Customer (JSON-String)

// -----------------------------------------------------------------------------
// Variante B — Raw-Form (robust, 3-Step, mehrparametrige @alias-Bindung)
// -----------------------------------------------------------------------------
// AddCustomer → @custId → CreateOrder(customerId=@custId, total=99.90) → @orderId
// → GetOrder(@orderId). CreateOrder hat ZWEI Parameter, davon einer @alias —
// deshalb setzen wir parameterName auf den echten Parameternamen ("customerId"),
// damit der Server nach Name bindet (sicherer als positional).
const chain: HrpcRequest[] = [
  {
    controller: "Customer",
    method: "AddCustomer",
    id: "step1",
    stringData: JSON.stringify([
      { parameterName: "name", num: 0, data: JSON.stringify("Alice") },
      { parameterName: "email", num: 1, data: JSON.stringify("alice@x.com") },
    ]),
    dependencyMapping: { custId: "$" }, // neue CustomerId weitergeben
  },
  {
    controller: "Order",
    method: "CreateOrder",
    id: "step2",
    stringData: JSON.stringify([
      // data: "@custId" → Server erkennt @-Präfix und substituiert.
      { parameterName: "customerId", num: 0, data: "@custId" },
      { parameterName: "total", num: 1, data: JSON.stringify(99.9) },
    ]),
    dependencyMapping: { orderId: "$" }, // neue OrderId weitergeben
  },
  {
    controller: "Order",
    method: "GetOrderById",
    id: "step3",
    stringData: JSON.stringify([{ parameterName: "id", num: 0, data: "@orderId" }]),
  },
];

const [c, o, loaded] = await rest.callBatch(chain, ExecutionMode.Serial);
// c.data      = neue CustomerId (number)
// o.data      = neue OrderId (number)
// loaded.data = die angelegte Order ({ id, customerId, total, createdAt })

// -----------------------------------------------------------------------------
// Gotchas
// -----------------------------------------------------------------------------
// • JsonPath ist ergebnisrelativ (kein $.data-Envelope): "$", "$.Id", "$[0].Id".
// • Ein UNAUFGELÖSTES @alias → Server antwortet 400 "Unresolved dependencies".
//   Jeder @alias muss VOR seiner Nutzung deklariert sein (dependencyMapping eines
//   früheren Requests in derselben Batch).
// • Zirkuläre Abhängigkeiten → 400 für ALLE Requests der Batch.
// • Binäre Rückgabewerte (content, base64) liefern kein data → kein JsonPath/@alias.