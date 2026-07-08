import { HrpcError, CancelledError } from "./errors.js";
import { ExecutionMode } from "./types.js";
import type { HrpcMultiRequest, HrpcRequest, HrpcResponse } from "./types.js";
import { fromBase64, normalizeResponse, normalizeResponses } from "./request.js";

const READY_CONNECTING = 0;
const READY_OPEN = 1;
const READY_CLOSING = 2;
const READY_CLOSED = 3;

/** Minimale, browser-kompatible WebSocket-Schnittstelle (auch Node `ws`). */
export interface IWebSocket {
  readonly readyState: number;
  send(data: string): void;
  close(code?: number, reason?: string): void;
  onopen: (() => void) | null;
  onmessage: ((ev: { data: string | ArrayBuffer }) => void) | null;
  onclose: ((ev: { code?: number; reason?: string }) => void) | null;
  onerror: ((ev: unknown) => void) | null;
}

/** Fabrik für einen WebSocket (Browser global, Node `ws`, oder Injektion für Tests). */
export type WsFactory = (
  url: string,
  options: { headers?: Record<string, string>; protocols?: string | string[] },
) => IWebSocket;

/** Per-Call-Optionen für einen WebSocket-Aufruf. */
export interface WsCallOptions {
  signal?: AbortSignal;
  timeout?: number;
}

/** Optionen für den WebSocket-Client. */
export interface HrpcWebSocketClientOptions {
  /** WS-Pfad (Default "hrpcws"). */
  wsPath?: string;
  /** Bearer-Token (Node: als Authorization-Header; Browser: als ?access_token=). */
  bearer?: string;
  /** Call-Timeout in ms. */
  callTimeout?: number;
  /** Connect-Timeout in ms (Default 15000). */
  connectTimeout?: number;
  /** Injizierbare WebSocket-Fabrik (Tests). */
  WebSocketCtor?: WsFactory;
}

interface PendingCall {
  resolve: (v: HrpcResponse | HrpcResponse[]) => void;
  reject: (e: Error) => void;
  isBatch: boolean;
  timer?: ReturnType<typeof setTimeout>;
  onCallerAbort?: () => void;
  callerSignal?: AbortSignal;
}

let _defaultFactoryPromise: Promise<WsFactory> | undefined;

/** Löst die Standard-WebSocket-Fabrik (Browser global bzw. Node `ws`) lazily auf. */
async function resolveDefaultFactory(): Promise<WsFactory> {
  if (_defaultFactoryPromise) return _defaultFactoryPromise;
  _defaultFactoryPromise = (async () => {
    if (typeof (globalThis as any).WebSocket !== "undefined") {
      return (url, opts) =>
        new (globalThis as any).WebSocket(url, opts?.protocols) as IWebSocket;
    }
    // Node: `ws` als optionalDependency; lazy geladen.
    const mod: any = await import("ws");
    const WS = mod.WebSocket ?? mod.default?.WebSocket ?? mod.default;
    if (typeof WS !== "function") {
      throw new HrpcError(
        0,
        "No WebSocket implementation found. In Node, install the optional 'ws' package.",
      );
    }
    return (url, opts) => new WS(url, opts?.protocols, { headers: opts?.headers }) as IWebSocket;
  })();
  return _defaultFactoryPromise;
}

/**
 * WebSocket-Client für Hrpc (RFC 6455 + JSON-Text-Frames), isomorph.
 *
 * Connect-Race (B1): konkurrierende `call()` erwarten denselben in-flight
 * Connect-Promise statt abgewiesen zu werden. Correlation (B3): jede Antwort
 * wird per `id` (Single) bzw. `requests[0].id` (Batch) zugeordnet; bei keinem
 * Match wird verworfen (kein Last-Resort-Fehl-Zuweisen).
 *
 * `call`/`callBatch` liefern die rohe Response (werfen nur bei Transport/Abbruch);
 * `callJson`/`callBinary` werfen bei logischem Nicht-2xx (Spiegel C#).
 *
 * **Known Limitation:** Der Server authentifiziert den WS-Upgrade nur über den
 * HTTP-Authorization-Header. Browser-WebSocket kann keine Header setzen —
 * authentifizierte Browser-WS-Calls brauchen serverseitige `?access_token=`-
 * Unterstützung (Roadmap) oder REST. Node (`ws`) sendet den Header korrekt.
 */
export class HrpcWebSocketClient {
  private readonly _baseUrl: string;
  private readonly _wsPath: string;
  private readonly _bearer?: string;
  private readonly _callTimeout?: number;
  private readonly _connectTimeout: number;
  private readonly _wsCtor?: WsFactory;

  private _ws?: IWebSocket;
  private _connectPromise?: Promise<void>;
  private _pending = new Map<string, PendingCall>();

  constructor(baseUrl: string, options: HrpcWebSocketClientOptions = {}) {
    if (!baseUrl || baseUrl.trim().length === 0) {
      throw new Error("HrpcWebSocketClient: baseUrl darf nicht leer sein.");
    }
    this._baseUrl = baseUrl.replace(/\/+$/, "");
    this._wsPath = (options.wsPath ?? "hrpcws").replace(/^\/+|\/+$/g, "");
    this._bearer = options.bearer;
    this._callTimeout = options.callTimeout;
    this._connectTimeout = options.connectTimeout ?? 15000;
    this._wsCtor = options.WebSocketCtor;
  }

  /** Stellt eine offene Verbindung sicher (B1: concurrent-safe). */
  async connect(): Promise<void> {
    if (this._ws && this._ws.readyState === READY_OPEN) return;
    if (this._connectPromise) return this._connectPromise;
    this._connectPromise = this.connectSlow().finally(() => {
      this._connectPromise = undefined;
    });
    return this._connectPromise;
  }

  /** Sendet einen einzelnen Request. */
  async call(req: HrpcRequest, opts?: WsCallOptions): Promise<HrpcResponse> {
    if (!req.id) req.id = `${req.controller}.${req.method}`;
    await this.connect();
    return this.sendAndAwait(req, false, opts) as Promise<HrpcResponse>;
  }

  /** Sendet einen Batch (Multi-Request). Auto-Setzt leere Ids. */
  async callBatch(
    requests: HrpcRequest[],
    mode: ExecutionMode = ExecutionMode.Parallel,
    opts?: WsCallOptions,
  ): Promise<HrpcResponse[]> {
    const normalized = requests.map((r) =>
      r.id ? r : { ...r, id: `${r.controller}.${r.method}` },
    );
    await this.connect();
    const multi: HrpcMultiRequest = { requests: normalized, mode };
    const key = normalized[0]?.id;
    if (!key) throw new HrpcError(0, "Batch requires at least one request with an id.");
    return this.sendAndAwait(multi as unknown as HrpcRequest, true, opts, key) as Promise<
      HrpcResponse[]
    >;
  }

  /** Ruft auf und deserialisiert `response.data` als T. Wirft bei Nicht-2xx. */
  async callJson<T>(req: HrpcRequest, opts?: WsCallOptions): Promise<T | null> {
    const response = await this.call(req, opts);
    return parseData<T>(response);
  }

  /** Ruft eine byte[]-Methode auf; liefert `response.content` als Uint8Array. Wirft bei Nicht-2xx. */
  async callBinary(req: HrpcRequest, opts?: WsCallOptions): Promise<Uint8Array | null> {
    const response = await this.call(req, opts);
    if (!response.isSuccess) throw HrpcError.fromResponse(response);
    return response.content ? fromBase64(response.content) : null;
  }

  /** Schließt die Verbindung; alle pending Calls werden mit HrpcError abgelehnt. */
  close(): void {
    this.rejectAllPending(new HrpcError(0, "WebSocket closed by client."));
    if (this._ws) {
      try {
        this._ws.close(1000, "client close");
      } catch {
        // ignore
      }
    }
    this._ws = undefined;
  }

  /** Alias für {@link close} (Symmetrie zum REST-Client). */
  dispose(): void {
    this.close();
  }

  // --- Interna ---

  private async connectSlow(): Promise<void> {
    const factory = this._wsCtor ?? (await resolveDefaultFactory());
    const isBrowserWs = typeof (globalThis as any).WebSocket !== "undefined";
    const url = this.buildUrl(isBrowserWs);
    const headers =
      !isBrowserWs && this._bearer ? { Authorization: `Bearer ${this._bearer}` } : undefined;
    const ws = factory(url, { headers });
    this._ws = ws;

    await new Promise<void>((resolve, reject) => {
      let opened = false;
      let connectTimer: ReturnType<typeof setTimeout> | undefined;

      const fail = (err: Error) => {
        if (connectTimer) clearTimeout(connectTimer);
        if (!opened) reject(err);
      };

      connectTimer = setTimeout(
        () => fail(new HrpcError(0, "WebSocket connect timed out.")),
        this._connectTimeout,
      );

      ws.onopen = () => {
        opened = true;
        if (connectTimer) clearTimeout(connectTimer);
        resolve();
      };
      ws.onmessage = (ev) => this.onMessage(ev.data);
      ws.onclose = (ev) => {
        this.onClosed();
        if (!opened) {
          fail(new HrpcError(0, `WebSocket closed before open (code ${ev?.code ?? "n/a"}).`));
        }
      };
      ws.onerror = () => {
        if (!opened) fail(new HrpcError(0, "WebSocket connection failed."));
        // nach open folgt onclose, das alle pending ablehnt.
      };
    });
  }

  private buildUrl(isBrowserWs: boolean): string {
    let base = this._baseUrl;
    base = base.replace(/^http:/i, "ws:").replace(/^https:/i, "wss:");
    let url = `${base}/${this._wsPath}`;
    if (isBrowserWs && this._bearer) {
      url += `?access_token=${encodeURIComponent(this._bearer)}`;
    }
    return url;
  }

  private sendAndAwait(
    payload: HrpcRequest,
    isBatch: boolean,
    opts: WsCallOptions | undefined,
    explicitKey?: string,
  ): Promise<HrpcResponse | HrpcResponse[]> {
    const key = explicitKey ?? payload.id!;
    const deferred = this.registerPending(key, isBatch, opts);
    try {
      const ws = this._ws;
      if (!ws || ws.readyState !== READY_OPEN) {
        this.disposePending(key);
        return Promise.reject(new HrpcError(0, "WebSocket is not open."));
      }
      ws.send(JSON.stringify(payload));
      return deferred.promise;
    } catch (err) {
      this.disposePending(key);
      return Promise.reject(
        err instanceof HrpcError
          ? err
          : new HrpcError(0, `WebSocket send error: ${(err as Error)?.message ?? err}`),
      );
    }
  }

  private registerPending(
    key: string,
    isBatch: boolean,
    opts: WsCallOptions | undefined,
  ): { promise: Promise<HrpcResponse | HrpcResponse[]> } {
    let resolve!: (v: HrpcResponse | HrpcResponse[]) => void;
    let reject!: (e: Error) => void;
    const promise = new Promise<HrpcResponse | HrpcResponse[]>((res, rej) => {
      resolve = res;
      reject = rej;
    });

    const pending: PendingCall = { resolve, reject, isBatch };
    const timeoutMs = opts?.timeout ?? this._callTimeout;

    if (timeoutMs && timeoutMs > 0) {
      pending.timer = setTimeout(
        () => this.rejectPending(key, new CancelledError("Hrpc call timed out.", true)),
        timeoutMs,
      );
    }

    if (opts?.signal) {
      if (opts.signal.aborted) {
        // Sofort abgelehnt (unverpackt).
        queueMicrotask(() => this.rejectPending(key, new CancelledError("Hrpc call was cancelled.")));
      } else {
        pending.callerSignal = opts.signal;
        pending.onCallerAbort = () =>
          this.rejectPending(key, new CancelledError("Hrpc call was cancelled."));
        opts.signal.addEventListener("abort", pending.onCallerAbort, { once: true });
      }
    }

    this._pending.set(key, pending);
    return { promise };
  }

  private rejectPending(key: string, err: Error): void {
    const pending = this._pending.get(key);
    if (!pending) return;
    if (pending.timer) clearTimeout(pending.timer);
    if (pending.onCallerAbort && pending.callerSignal) {
      pending.callerSignal.removeEventListener("abort", pending.onCallerAbort);
    }
    this._pending.delete(key);
    pending.reject(err);
  }

  /** Räumt einen pending Call auf, ohne ihn abzulehnen (Sendefehler-Pfad). */
  private disposePending(key: string): void {
    const pending = this._pending.get(key);
    if (!pending) return;
    if (pending.timer) clearTimeout(pending.timer);
    if (pending.onCallerAbort && pending.callerSignal) {
      pending.callerSignal.removeEventListener("abort", pending.onCallerAbort);
    }
    this._pending.delete(key);
  }

  private resolvePending(key: string, value: HrpcResponse | HrpcResponse[]): boolean {
    const pending = this._pending.get(key);
    if (!pending) return false;
    if (pending.timer) clearTimeout(pending.timer);
    if (pending.onCallerAbort && pending.callerSignal) {
      pending.callerSignal.removeEventListener("abort", pending.onCallerAbort);
    }
    this._pending.delete(key);
    pending.resolve(value);
    return true;
  }

  private rejectAllPending(err: Error): void {
    for (const key of [...this._pending.keys()]) this.rejectPending(key, err);
  }

  private onMessage(data: string | ArrayBuffer): void {
    const text = typeof data === "string" ? data : new TextDecoder().decode(data);
    let parsed: unknown;
    try {
      parsed = JSON.parse(text);
    } catch {
      // Server-Fehlerframes ohne id können nicht korreliert werden -> verwerfen.
      return;
    }

    if (Array.isArray(parsed)) {
      // Batch-Response: Korrelation über das erste Element.
      const arr = normalizeResponses(parsed as HrpcResponse[]);
      const key = arr[0]?.id ?? undefined;
      if (key && this.resolvePending(key, arr)) return;
      this.dropUnmatched(text, key);
      return;
    }

    const resp = normalizeResponse(parsed as HrpcResponse);
    const key = resp?.id ?? undefined;
    if (key && this.resolvePending(key, resp)) return;
    this.dropUnmatched(text, key);
  }

  private dropUnmatched(text: string, key: string | undefined): void {
    // B3: kein Last-Resort — nicht zuordnen, verwerfen. Der pending Caller läuft
    // über seinen Timeout/sein Signal ab.
    console.warn(
      `[hrpc-client] Received WebSocket response with no matching pending request (id=${key ?? "n/a"}). Dropping.`,
    );
    void text;
  }

  private onClosed(): void {
    this._ws = undefined;
    this.rejectAllPending(new HrpcError(0, "WebSocket connection closed."));
  }
}

// --- Shared (gleichlautend mit rest.ts) ---

function parseData<T>(response: HrpcResponse): T | null {
  if (response.isSuccess && typeof response.data === "string" && response.data.length > 0) {
    try {
      return JSON.parse(response.data) as T;
    } catch (err) {
      throw new HrpcError(0, "Failed to deserialize the response.", { cause: err });
    }
  }
  if (!response.isSuccess) throw HrpcError.fromResponse(response);
  return null;
}