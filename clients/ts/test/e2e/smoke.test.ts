import { describe, it, expect } from "vitest";
import { createClient, HrpcCall } from "../../src/index.js";
import type { HrpcRestClient, HrpcWebSocketClient } from "../../src/index.js";

/**
 * End-to-End-Smoke-Test gegen die laufende Sample-App (Hrpc).
 *
 * Opt-in: nur aktiv, wenn HRPC_E2E=1 gesetzt ist. Startet nichts selbst —
 * erwartet einen laufenden Server unter HRPC_BASE_URL (Default
 * http://localhost:5052, wie launchSettings). Node-seitiges fetch/`ws` lösen
 * CORS nicht aus (im Gegensatz zum Browser).
 *
 * Aufruf:
 *   dotnet run --project Hrpc   # in einem separaten Terminal
 *   HRPC_E2E=1 npm run test:e2e
 */
const ENABLED = process.env.HRPC_E2E === "1";
const BASE = process.env.HRPC_BASE_URL ?? "http://localhost:5052";
const { rest, ws } = createClient(BASE);

const itIf = ENABLED ? it : it.skip;

interface AdresseX {
  Id: number;
  Name: string;
  Age: number;
  Greet: string;
}

describe("E2E gegen Sample-App", () => {
  itIf("REST: discovery listet TestService auf", async () => {
    const meta = await rest.discover();
    expect(meta.controllers.map((c) => c.name)).toContain("TestService");
  });

  itIf("REST: TestService.GetAdresse liefert die Echo-Werte", async () => {
    const a = await rest.callJson<AdresseX>("TestService", "GetAdresse", {
      id: 1,
      greet: "hi",
    });
    expect(a?.Id).toBe(1);
    expect(a?.Greet).toBe("hi");
  });

  itIf("REST: Batch mit zwei parallelen Calls", async () => {
    const req = (id: number) =>
      HrpcCall.init("TestService", "GetAdresse")
        .with({ id, greet: "g" })
        .named(`e2e-${id}`)
        .toRequest();
    const arr = await rest.callBatch([req(1), req(2)]);
    expect(arr).toHaveLength(2);
    expect(arr.every((r) => r.isSuccess)).toBe(true);
  });

  itIf("WebSocket: TestService.GetAdresse über persistente Verbindung", async () => {
    await (ws as HrpcWebSocketClient).connect();
    try {
      const a = await (ws as HrpcWebSocketClient).callJson<AdresseX>(
        HrpcCall.init("TestService", "GetAdresse").with({ id: 2, greet: "ws" }).toRequest(),
      );
      expect(a?.Id).toBe(2);
      expect(a?.Greet).toBe("ws");
    } finally {
      (ws as HrpcWebSocketClient).close();
    }
  });
});

// Statische Referenz, damit ungenutzte Imports bei Skip kein Lint-Problem werden.
void (rest as HrpcRestClient);
void (ws as HrpcWebSocketClient);