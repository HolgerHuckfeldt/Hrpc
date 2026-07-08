import { describe, it, expect, vi } from "vitest";
import { HrpcWebSocketClient } from "../../src/websocket.js";
import type { IWebSocket, WsFactory } from "../../src/websocket.js";
import { HrpcError, CancelledError } from "../../src/errors.js";
import { ExecutionMode } from "../../src/types.js";

const OPEN = 1;

/** Browser-ähnlicher Mock-WebSocket mit steuerbaren Event-Feuern. */
class MockWs implements IWebSocket {
  readyState = 0; // CONNECTING
  onopen: (() => void) | null = null;
  onmessage: ((ev: { data: string | ArrayBuffer }) => void) | null = null;
  onclose: ((ev: { code?: number; reason?: string }) => void) | null = null;
  onerror: ((ev: unknown) => void) | null = null;
  readonly sent: string[] = [];
  readonly url: string;
  readonly opts: any;
  constructor(url: string, opts: any) {
    this.url = url;
    this.opts = opts;
  }
  send(data: string): void {
    this.sent.push(data);
  }
  close(code?: number, reason?: string): void {
    this.readyState = 3;
  }
  fireOpen(): void {
    this.readyState = OPEN;
    this.onopen?.();
  }
  fireMessage(data: string): void {
    this.onmessage?.({ data });
  }
  fireClose(code = 1000): void {
    this.readyState = 3;
    this.onclose?.({ code });
  }
  fireError(): void {
    this.onerror?.({});
  }
  get lastSent(): string | undefined {
    return this.sent[this.sent.length - 1];
  }
}

/**
 * Stabiler Holder: die Factory mutiert `ref.ws`/`ref.created` synchron während
 * `client.call(...)`. Tests lesen die Werte NACH dem Auslösen der Calls, sodass
 * die Mutation sichtbar wird (Destructuring würde den Wert zum falschen Zeitpunkt
 * einfrieren — deshalb Holder statt Getter).
 */
interface ClientCtx {
  client: HrpcWebSocketClient;
  ref: { ws: MockWs | undefined; created: number };
}

function makeClient(): ClientCtx {
  const ref: { ws: MockWs | undefined; created: number } = { ws: undefined, created: 0 };
  const factory: WsFactory = (url, opts) => {
    ref.created++;
    ref.ws = new MockWs(url, opts);
    return ref.ws;
  };
  const client = new HrpcWebSocketClient("http://127.0.0.1:0", { WebSocketCtor: factory });
  return { client, ref };
}

function resp(id: string, data: string): string {
  return JSON.stringify({ code: 200, data, id, isSuccess: true });
}

describe("HrpcWebSocketClient", () => {
  it("concurrent calls teilen sich EINEN Connect (B1) und korrelieren per id (B3)", async () => {
    const { client, ref } = makeClient();
    const calls = [0, 1, 2, 3, 4].map((i) =>
      client.call({ controller: "C", method: "M", stringData: "[]", id: `r${i}` }),
    );
    expect(ref.created).toBe(1); // nur ein WebSocket trotz 5 paralleler Calls
    const ws = ref.ws!;
    ws.fireOpen();
    await vi.waitFor(() => expect(ws.sent.length).toBe(5), { interval: 1, timeout: 1000 });
    // Antworten in verdrehter Reihenfolge — jeder Call bekommt sein eigenes Resultat.
    for (const i of [4, 2, 0, 3, 1]) ws.fireMessage(resp(`r${i}`, `"v${i}"`));
    const results = await Promise.all(calls);
    expect(results.map((r) => r.data)).toEqual([
      '"v0"',
      '"v1"',
      '"v2"',
      '"v3"',
      '"v4"',
    ]);
  });

  it("verwirft Responses ohne passenden pending Call (B3, kein Last-Resort)", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const { client, ref } = makeClient();
    const p = client.call({ controller: "C", method: "M", stringData: "[]", id: "a" });
    const ws = ref.ws!;
    ws.fireOpen();
    await vi.waitFor(() => expect(ws.sent.length).toBe(1), { interval: 1, timeout: 1000 });
    ws.fireMessage(resp("unknown-id", '"x"')); // kein Match -> drop, kein Reject
    const settled = await Promise.race([
      p.then(() => "resolved"),
      Promise.resolve("pending"),
    ]);
    expect(settled).toBe("pending");
    ws.fireMessage(resp("a", '"ok"')); // Match -> resolved
    const r = await p;
    expect(r.data).toBe('"ok"');
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });

  it("korreliert Batch per requests[0].id und liefert Array", async () => {
    const { client, ref } = makeClient();
    const p = client.callBatch(
      [
        { controller: "C", method: "M", stringData: "[]", id: "b0" },
        { controller: "C", method: "M", stringData: "[]", id: "b1" },
      ],
      ExecutionMode.Serial,
    );
    const ws = ref.ws!;
    ws.fireOpen();
    await vi.waitFor(() => expect(ws.sent.length).toBe(1), { interval: 1, timeout: 1000 });
    // Server liefert Array; Korrelation über erstes Element.
    ws.fireMessage(JSON.stringify([resp("b0", '"0"'), resp("b1", '"1"')].map((s) => JSON.parse(s))));
    const arr = await p;
    expect(arr).toHaveLength(2);
    expect(arr[0].data).toBe('"0"');
    expect(arr[1].data).toBe('"1"');
  });

  it("Timeout -> CancelledError mit timedOut=true", async () => {
    const { client, ref } = makeClient();
    const p = client.call(
      { controller: "C", method: "M", stringData: "[]", id: "t" },
      { timeout: 20 },
    );
    const ws = ref.ws!;
    ws.fireOpen();
    await vi.waitFor(() => expect(ws.sent.length).toBe(1), { interval: 1, timeout: 1000 });
    await expect(p).rejects.toMatchObject({ name: "CancelledError", timedOut: true });
  });

  it("Caller-Abbruch -> CancelledError (unverpackt)", async () => {
    const { client, ref } = makeClient();
    const ac = new AbortController();
    ac.abort();
    const p = client.call(
      { controller: "C", method: "M", stringData: "[]", id: "c" },
      { signal: ac.signal },
    );
    // Der Client lehnt bereits abgebrochene Signale per Microtask ab. Im realen
    // Gebrauch hängt `await client.call(...)` den Handler synchron an; hier wird
    // p aber über vi.waitFor gehalten, daher hängen wir früh einen Handler an,
    // damit die Microtask-Rejection nicht als unhandled durchgeht.
    p.catch(() => {});
    const ws = ref.ws!;
    ws.fireOpen();
    await vi.waitFor(() => expect(ws.sent.length).toBe(1), { interval: 1, timeout: 1000 });
    await expect(p).rejects.toBeInstanceOf(CancelledError);
  });

  it("Schließen der Verbindung lehnt pending Calls mit HrpcError ab", async () => {
    const { client, ref } = makeClient();
    const p = client.call({ controller: "C", method: "M", stringData: "[]", id: "p" });
    const ws = ref.ws!;
    ws.fireOpen();
    await vi.waitFor(() => expect(ws.sent.length).toBe(1), { interval: 1, timeout: 1000 });
    ws.fireClose(1001);
    await expect(p).rejects.toBeInstanceOf(HrpcError);
  });

  it("leitet isSuccess aus code ab, wenn der Server es nicht sendet (Wire-Fakt)", async () => {
    const { client, ref } = makeClient();
    const p = client.call({ controller: "C", method: "M", stringData: "[]", id: "d" });
    const ws = ref.ws!;
    ws.fireOpen();
    await vi.waitFor(() => expect(ws.sent.length).toBe(1), { interval: 1, timeout: 1000 });
    // Server sendet KEIN isSuccess-Feld ([JsonIgnore], aus code berechnet).
    ws.fireMessage(JSON.stringify({ code: 200, data: '"ok"', id: "d" }));
    const r = await p;
    expect(r.isSuccess).toBe(true); // vom Client aus code abgeleitet
    expect(r.data).toBe('"ok"');
  });

  it("baut die WS-URL aus baseUrl + wsPath (http->ws)", async () => {
    let url = "";
    let created = 0;
    let ws!: MockWs;
    const factory: WsFactory = (u) => {
      url = u;
      created++;
      ws = new MockWs(u, {});
      return ws;
    };
    const client = new HrpcWebSocketClient("https://host:5001/app/", {
      WebSocketCtor: factory,
      wsPath: "hrpcws",
    });
    const p = client.call({ controller: "C", method: "M", stringData: "[]", id: "x" });
    // factory wird synchron beim Connect angelegt
    expect(created).toBe(1);
    expect(url).toBe("wss://host:5001/app/hrpcws");
    // aufräumen: Connect ablehnen (Mock-close feuert onclose -> Connect-Promise rejected).
    client.dispose();
    ws.fireClose();
    await p.catch(() => {});
  });
});