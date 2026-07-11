import { describe, it, expect } from "vitest";
import { createClient, WaevaRpcCall } from "../../src/index.js";
import type { WaevaRpcRestClient, WaevaRpcWebSocketClient } from "../../src/index.js";

/**
 * End-to-End-Smoke-Test gegen die laufende Sample-App (WaevaRpc).
 *
 * Opt-in: nur aktiv, wenn WAEVARPC_E2E=1 gesetzt ist. Startet nichts selbst —
 * erwartet einen laufenden Server unter WAEVARPC_BASE_URL (Default
 * http://localhost:5052, wie launchSettings). Node-seitiges fetch/`ws` lösen
 * CORS nicht aus (im Gegensatz zum Browser).
 *
 * Aufruf:
 *   dotnet run --project WaevaRpc   # in einem separaten Terminal
 *   WAEVARPC_E2E=1 npm run test:e2e
 */
const ENABLED = process.env.WAEVARPC_E2E === "1";
const BASE = process.env.WAEVARPC_BASE_URL ?? "http://localhost:5052";
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
      WaevaRpcCall.init("TestService", "GetAdresse")
        .with({ id, greet: "g" })
        .named(`e2e-${id}`)
        .toRequest();
    const arr = await rest.callBatch([req(1), req(2)]);
    expect(arr).toHaveLength(2);
    expect(arr.every((r) => r.isSuccess)).toBe(true);
  });

  itIf("WebSocket: TestService.GetAdresse über persistente Verbindung", async () => {
    await (ws as WaevaRpcWebSocketClient).connect();
    try {
      const a = await (ws as WaevaRpcWebSocketClient).callJson<AdresseX>(
        WaevaRpcCall.init("TestService", "GetAdresse").with({ id: 2, greet: "ws" }).toRequest(),
      );
      expect(a?.Id).toBe(2);
      expect(a?.Greet).toBe("ws");
    } finally {
      (ws as WaevaRpcWebSocketClient).close();
    }
  });
});

// Statische Referenz, damit ungenutzte Imports bei Skip kein Lint-Problem werden.
void (rest as WaevaRpcRestClient);
void (ws as WaevaRpcWebSocketClient);