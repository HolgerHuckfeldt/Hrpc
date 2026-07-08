import type { HrpcErrorBody, HrpcResponse } from "./types.js";

/**
 * Fehler bei einem Hrpc-Aufruf. Spiegelt das C#-Äquivalent (HrpcException).
 *
 * - **Logischer Fehler** (non-2xx `code` im Response-Body): `code`/`message`/
 *   `details`/`requestId` aus HrpcResponse.error (oder aus Response abgeleitet).
 * - **Transportfehler** (Netzwerk, non-200 HTTP, malformed JSON): `code` = 0,
 *   `message` beschreibt den Transportfehler, ursprüngliche Exception in `cause`.
 *
 * Cancellation (AbortSignal) wird **nicht** als HrpcError geworfen, sondern als
 * {@link CancelledError} — konsistent mit der C#-Konvention (OCE unverpackt).
 */
export class HrpcError extends Error {
  readonly code: number;
  readonly details?: string | null;
  readonly requestId?: string | null;

  constructor(
    code: number,
    message: string,
    options?: { details?: string | null; requestId?: string | null; cause?: unknown },
  ) {
    super(message, options?.cause !== undefined ? { cause: options.cause } : undefined);
    this.name = "HrpcError";
    this.code = code;
    this.details = options?.details;
    this.requestId = options?.requestId;
  }

  /** Baut einen HrpcError aus einem HrpcErrorBody (z. B. response.error). */
  static fromBody(body: HrpcErrorBody): HrpcError {
    return new HrpcError(body.code, body.message, {
      details: body.details,
      requestId: body.requestId,
    });
  }

  /**
   * Baut einen HrpcError aus einer nicht-erfolgreichen Response. Ist ein
   * strukturiertes `error` vorhanden, wird es genutzt; sonst wird eines aus
   * `code`/`data` abgeleitet (Spiegel von C# HrpcError.FromResponse).
   */
  static fromResponse(response: HrpcResponse): HrpcError {
    if (response.error) return HrpcError.fromBody(response.error);
    const message =
      typeof response.data === "string" && response.data.length > 0
        ? response.data
        : `Hrpc call failed with code ${response.code}.`;
    return new HrpcError(response.code, message, {
      requestId: response.id,
    });
  }
}

/**
 * Signalisiert Abbruch eines Aufrufs (AbortSignal/Timeout). Wird — anders als
 * HrpcError — unverpackt propagiert, damit Aufrufer Cancellation von echten
 * Fehlern unterscheiden können (Spiegel der C# OperationCanceledException).
 */
export class CancelledError extends Error {
  readonly timedOut: boolean;

  constructor(message = "Hrpc call was cancelled.", timedOut = false) {
    super(message);
    this.name = "CancelledError";
    this.timedOut = timedOut;
  }
}

/** True, wenn x ein Abbruch ist (CancelledError oder fetch-AbortError/DOMException). */
export function isCancelled(x: unknown): boolean {
  return (
    x instanceof CancelledError ||
    (x instanceof Error &&
      (x.name === "AbortError" || x.name === "CancelledError"))
  );
}