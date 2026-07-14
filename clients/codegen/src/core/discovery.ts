// Discovery loading + shape assertion.
//
// `loadDiscovery` accepts a URL (live server), a file path, or `-` (stdin) and
// returns a parsed DiscoveryInfo. `assertDiscoveryShape` is the no-version-field
// mitigation: discovery has no schema version yet, so we refuse malformed input
// loudly (English) rather than emit broken stubs from a structurally-wrong payload.

import { readFile } from "node:fs/promises";
import { TrameRestClient } from "trame-client";
import type { DiscoveryInfo } from "trame-client";

export interface LoadDiscoveryOptions {
  /** Bearer token when loading from a URL. */
  bearer?: string;
  /** Request timeout in ms (URL source). */
  timeout?: number;
  /** Abort signal. */
  signal?: AbortSignal;
}

/** Load discovery from a URL (`http(s)://…`), a file path, or `-` (stdin). */
export async function loadDiscovery(
  source: string,
  opts: LoadDiscoveryOptions = {},
): Promise<DiscoveryInfo> {
  if (!source || source === "-") {
    return loadFromStdin();
  }
  if (/^https?:\/\//i.test(source)) {
    return loadFromUrl(source, opts);
  }
  return loadFromFile(source);
}

async function loadFromUrl(url: string, opts: LoadDiscoveryOptions): Promise<DiscoveryInfo> {
  // Derive baseUrl + apiPath from a discovery URL of the form
  // `<origin>/api/trame/discovery` (the trailing `/discovery` is stripped, the
  // leading path becomes apiPath). The TrameRestClient normalizes both.
  const withoutDiscovery = url.replace(/\/discovery\/?$/i, "");
  const parsed = new URL(withoutDiscovery);
  const baseUrl = `${parsed.protocol}//${parsed.host}/`;
  const apiPath = parsed.pathname.replace(/^\/+|\/+$/g, "");
  const client = new TrameRestClient(baseUrl, {
    apiPath: apiPath || undefined,
    bearer: opts.bearer,
    callTimeout: opts.timeout,
  });
  const payload = await client.discover({ signal: opts.signal });
  // Validate even the URL path so a malformed server response fails loudly
  // (no discovery schema version exists yet — refuse bad shape early).
  return assertDiscoveryShape(payload);
}

async function loadFromFile(path: string): Promise<DiscoveryInfo> {
  const text = await readFile(path, "utf8");
  return parseDiscovery(text, path);
}

async function loadFromStdin(): Promise<DiscoveryInfo> {
  const text = await readAllStdin();
  return parseDiscovery(text, "stdin");
}

function parseDiscovery(text: string, source: string): DiscoveryInfo {
  let obj: unknown;
  try {
    obj = JSON.parse(text);
  } catch (err) {
    throw new DiscoveryShapeError(
      `Failed to parse discovery JSON from ${source}: ${(err as Error).message}`,
    );
  }
  return assertDiscoveryShape(obj);
}

function readAllStdin(): Promise<string> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    process.stdin.on("data", (c: Buffer) => chunks.push(c));
    process.stdin.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    process.stdin.on("error", reject);
  });
}

/** Raised when the discovery payload does not match the expected shape. */
export class DiscoveryShapeError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "DiscoveryShapeError";
  }
}

/**
 * Runtime shape guard. Refuses malformed input loudly — the mitigation for the
 * absence of a discovery schema version. Throws DiscoveryShapeError (English)
 * with a precise reason.
 */
export function assertDiscoveryShape(obj: unknown): DiscoveryInfo {
  if (obj === null || typeof obj !== "object") {
    throw new DiscoveryShapeError("Discovery payload is not a JSON object.");
  }
  const o = obj as Record<string, unknown>;
  if (!Array.isArray(o.controllers)) {
    throw new DiscoveryShapeError(
      'Discovery payload is missing a "controllers" array (expected { controllers: [...], types: {...} }).',
    );
  }
  if (o.types === null || typeof o.types !== "object" || Array.isArray(o.types)) {
    throw new DiscoveryShapeError(
      'Discovery payload is missing a "types" object (expected { controllers: [...], types: {...} }).',
    );
  }
  // Spot-check one controller + method so a structurally-wrong-but-object
  // payload (e.g. a TrameResponse envelope) is caught early.
  for (const c of o.controllers) {
    if (!c || typeof c !== "object") {
      throw new DiscoveryShapeError('A "controllers" entry is not an object.');
    }
    const ctrl = c as Record<string, unknown>;
    if (typeof ctrl.name !== "string") {
      throw new DiscoveryShapeError('A controller is missing a string "name".');
    }
    if (!Array.isArray(ctrl.methods)) {
      throw new DiscoveryShapeError(`Controller "${ctrl.name}" is missing a "methods" array.`);
    }
  }
  return obj as DiscoveryInfo;
}