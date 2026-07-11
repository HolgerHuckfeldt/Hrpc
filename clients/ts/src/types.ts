// Kanonische Wire-Typen des WaevaRpc-Protokolls (camelCase, siehe PROTOCOL.md).
// Port aus WaevaRpcDeveloperUi/src/lib/types/discovery.ts; Binary-Felder sind hier
// korrekt als base64-String getypt (System.Text.Json serialisiert byte[] als
// base64), nicht als number[].

/** Ausführungsmodus für Batch-Requests (WaevaRpcMultiRequest.mode). */
export enum ExecutionMode {
  /** 0 — alle Requests parallel (Dependencies werden ignoriert). */
  Parallel = 0,
  /** 1 — seriell, mit @alias-Abhängigkeitsauflösung (topologisch). */
  Serial = 1,
}

/** Lebenszyklus-Zustand des WebSocket-Clients (Spiegel von C# WaevaRpcConnectionState). */
export enum WaevaRpcConnectionState {
  /** 0 — keine aktive Verbindung (vor dem ersten Connect oder nach erschöpftem Reconnect). */
  Disconnected = 0,
  /** 1 — Verbindungsaufbau läuft. */
  Connecting = 1,
  /** 2 — Verbindung steht; Calls können gesendet werden. */
  Connected = 2,
  /** 3 — unerwarteter Disconnect; Hintergrund-Reconnect mit Backoff läuft. */
  Reconnecting = 3,
}

/** Strukturierter Fehler im WaevaRpcResponse.error-Feld (code != 2xx). */
export interface WaevaRpcErrorBody {
  code: number;
  message: string;
  details?: string | null;
  requestId?: string | null;
}

/** Ein einzelner Parameter innerhalb von WaevaRpcRequest.stringData. */
export interface WaevaRpcParameter {
  /** Parametername (Server bindet danach). Bei Positionalen leer/ein Platzhalter. */
  parameterName: string;
  /** JSON-String des Werts (z. B. "42", "\"hi\"", "{\"id\":1}"). */
  data: string;
  /** Positionaler Index (Fallback, wenn parameterName nicht bindet). */
  num?: number;
}

/** Einzelner RPC-Request. */
export interface WaevaRpcRequest {
  controller: string;
  method: string;
  /** JSON-String eines Arrays von WaevaRpcParameter. */
  stringData?: string;
  id?: string;
  /** alias → JsonPath; Werte aus dieser Response werden für Folgerequests exposed. */
  dependencyMapping?: Record<string, string> | null;
  /** base64-kodiertes Binary (für byte[]-Parameter der Zielmethode). */
  binaryData?: string | null;
}

/** Batch-Request (mehrere Calls in einem Roundtrip). */
export interface WaevaRpcMultiRequest {
  requests: WaevaRpcRequest[];
  mode: ExecutionMode;
}

/** Antwort eines RPC-Calls. */
export interface WaevaRpcResponse {
  /** Logischer Status-Code (im Body, nicht HTTP-Status). 200–299 = Erfolg. */
  code: number;
  /** Strukturierter Ergebniswert (roh, null bei 204/void/Fehler). Seit dem
   *  Single-Pass-Fix kein JSON-String mehr, sondern der geparste Wert. */
  data?: unknown | null;
  /** base64-kodiertes Binary-Result (für byte[]-Rückgaben). */
  content?: string | null;
  /** Korrelations-Id (spiegelt request.id). */
  id?: string | null;
  /** Aufgelöste alias → Wert-Map für Dependency-Chaining. */
  exposedDependencies?: Record<string, string> | null;
  /** Strukturierter Fehler bei non-2xx. */
  error?: WaevaRpcErrorBody | null;
  /**
   * true, wenn code 200–299. Server-seitig `[JsonIgnore]` und aus `code`
   * abgeleitet — das Wire-Frame enthält dieses Feld NICHT. Der Client füllt
   * es beim Parsen auf (siehe `normalizeResponse`); es ist daher optional.
   */
  isSuccess?: boolean;
}

// --- Discovery (GET /api/waeva/discovery) ---

export interface DiscoveryInfo {
  controllers: ControllerMeta[];
  types: Record<string, TypeMeta>;
}

export interface ControllerMeta {
  name: string;
  methods: MethodMeta[];
}

export interface MethodMeta {
  methodName: string;
  returnType: string;
  returnTypeDefinition?: TypeMeta | null;
  parameters: ParameterMeta[];
  documentation?: string | null;
}

export interface ParameterMeta {
  parameterName: string;
  parameterType: string;
  typeDefinition?: TypeMeta | null;
  documentation?: string | null;
}

export interface TypeMeta {
  typeName: string;
  properties: PropertyMeta[];
  example?: unknown;
}

export interface PropertyMeta {
  propertyName: string;
  propertyType: string;
  nestedTypeDefinition?: TypeMeta | null;
}