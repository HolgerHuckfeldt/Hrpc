// WaevaRpc JavaScript/TypeScript Client — öffentliche API.
// Siehe PROTOCOL.md für das Wire-Format und clients/ts/README.md für Nutzung.

export * from "./types.js";
export { WaevaRpcError, CancelledError, isCancelled } from "./errors.js";
export { WaevaRpcCall } from "./fluent.js";
export {
  WaevaRpcRestClient,
  type WaevaRpcRestClientOptions,
  type CallOptions,
} from "./rest.js";
export {
  WaevaRpcWebSocketClient,
  type WaevaRpcWebSocketClientOptions,
  type WsCallOptions,
  type IWebSocket,
  type WsFactory,
} from "./websocket.js";
export { buildStringData, buildSingle, buildMulti, toBase64, fromBase64 } from "./request.js";

import { WaevaRpcRestClient, type WaevaRpcRestClientOptions } from "./rest.js";
import {
  WaevaRpcWebSocketClient,
  type WaevaRpcWebSocketClientOptions,
} from "./websocket.js";

/** Gemeinsame Bearer/Timeout-Optionen für createClient. */
export interface CreateClientOptions {
  bearer?: string;
  callTimeout?: number;
  rest?: Omit<WaevaRpcRestClientOptions, "bearer" | "callTimeout">;
  ws?: Omit<WaevaRpcWebSocketClientOptions, "bearer" | "callTimeout">;
}

export interface WaevaRpcClient {
  rest: WaevaRpcRestClient;
  ws: WaevaRpcWebSocketClient;
}

/**
 * Convenience-Factory: erzeugt ein REST- und ein WebSocket-Client-Paar mit
 * gemeinsamen Bearer/Timeout-Optionen.
 *
 * ```ts
 * const { rest, ws } = createClient("https://localhost:5001", { bearer: token });
 * const customer = await rest.callJson<Customer>("Customer", "GetById", { id: 42 });
 * const wsClient = ws; await wsClient.connect();
 * ```
 */
export function createClient(baseUrl: string, options: CreateClientOptions = {}): WaevaRpcClient {
  const { bearer, callTimeout, rest, ws } = options;
  return {
    rest: new WaevaRpcRestClient(baseUrl, { ...rest, bearer, callTimeout }),
    ws: new WaevaRpcWebSocketClient(baseUrl, { ...ws, bearer, callTimeout }),
  };
}