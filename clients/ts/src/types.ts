// Kanonische Wire-Typen des Hrpc-Protokolls (camelCase, siehe PROTOCOL.md).
// Port aus HrpcDeveloperUi/src/lib/types/discovery.ts; Binary-Felder sind hier
// korrekt als base64-String getypt (System.Text.Json serialisiert byte[] als
// base64), nicht als number[].

/** Ausführungsmodus für Batch-Requests (HrpcMultiRequest.mode). */
export enum ExecutionMode {
  /** 0 — alle Requests parallel (Dependencies werden ignoriert). */
  Parallel = 0,
  /** 1 — seriell, mit @alias-Abhängigkeitsauflösung (topologisch). */
  Serial = 1,
}

/** Strukturierter Fehler im HrpcResponse.error-Feld (code != 2xx). */
export interface HrpcErrorBody {
  code: number;
  message: string;
  details?: string | null;
  requestId?: string | null;
}

/** Ein einzelner Parameter innerhalb von HrpcRequest.stringData. */
export interface HrpcParameter {
  /** Parametername (Server bindet danach). Bei Positionalen leer/ein Platzhalter. */
  parameterName: string;
  /** JSON-String des Werts (z. B. "42", "\"hi\"", "{\"id\":1}"). */
  data: string;
  /** Positionaler Index (Fallback, wenn parameterName nicht bindet). */
  num?: number;
}

/** Einzelner RPC-Request. */
export interface HrpcRequest {
  controller: string;
  method: string;
  /** JSON-String eines Arrays von HrpcParameter. */
  stringData?: string;
  id?: string;
  /** alias → JsonPath; Werte aus dieser Response werden für Folgerequests exposed. */
  dependencyMapping?: Record<string, string> | null;
  /** base64-kodiertes Binary (für byte[]-Parameter der Zielmethode). */
  binaryData?: string | null;
}

/** Batch-Request (mehrere Calls in einem Roundtrip). */
export interface HrpcMultiRequest {
  requests: HrpcRequest[];
  mode: ExecutionMode;
}

/** Antwort eines RPC-Calls. */
export interface HrpcResponse {
  /** Logischer Status-Code (im Body, nicht HTTP-Status). 200–299 = Erfolg. */
  code: number;
  /** JSON-String des Results (null bei 204/void). */
  data?: string | null;
  /** base64-kodiertes Binary-Result (für byte[]-Rückgaben). */
  content?: string | null;
  /** Korrelations-Id (spiegelt request.id). */
  id?: string | null;
  /** Aufgelöste alias → Wert-Map für Dependency-Chaining. */
  exposedDependencies?: Record<string, string> | null;
  /** Strukturierter Fehler bei non-2xx. */
  error?: HrpcErrorBody | null;
  /**
   * true, wenn code 200–299. Server-seitig `[JsonIgnore]` und aus `code`
   * abgeleitet — das Wire-Frame enthält dieses Feld NICHT. Der Client füllt
   * es beim Parsen auf (siehe `normalizeResponse`); es ist daher optional.
   */
  isSuccess?: boolean;
}

// --- Discovery (GET /api/hrpc/discovery) ---

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