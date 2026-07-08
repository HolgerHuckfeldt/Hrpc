// Hrpc JavaScript/TypeScript Client — öffentliche API.
// Siehe PROTOCOL.md für das Wire-Format und clients/ts/README.md für Nutzung.

export * from "./types.js";
export { HrpcError, CancelledError, isCancelled } from "./errors.js";
export { HrpcCall } from "./fluent.js";
export {
  HrpcRestClient,
  type HrpcRestClientOptions,
  type CallOptions,
} from "./rest.js";
export {
  HrpcWebSocketClient,
  type HrpcWebSocketClientOptions,
  type WsCallOptions,
  type IWebSocket,
  type WsFactory,
} from "./websocket.js";
export { buildStringData, buildSingle, buildMulti, toBase64, fromBase64 } from "./request.js";

import { HrpcRestClient, type HrpcRestClientOptions } from "./rest.js";
import {
  HrpcWebSocketClient,
  type HrpcWebSocketClientOptions,
} from "./websocket.js";

/** Gemeinsame Bearer/Timeout-Optionen für createClient. */
export interface CreateClientOptions {
  bearer?: string;
  callTimeout?: number;
  rest?: Omit<HrpcRestClientOptions, "bearer" | "callTimeout">;
  ws?: Omit<HrpcWebSocketClientOptions, "bearer" | "callTimeout">;
}

export interface HrpcClient {
  rest: HrpcRestClient;
  ws: HrpcWebSocketClient;
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
export function createClient(baseUrl: string, options: CreateClientOptions = {}): HrpcClient {
  const { bearer, callTimeout, rest, ws } = options;
  return {
    rest: new HrpcRestClient(baseUrl, { ...rest, bearer, callTimeout }),
    ws: new HrpcWebSocketClient(baseUrl, { ...ws, bearer, callTimeout }),
  };
}