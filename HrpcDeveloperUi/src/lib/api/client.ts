// Dünne Fassade über dem offiziellen Hrpc TS-Client (hrpc-client).
// Die DevUI dogfooded hier ihren eigenen Client statt einen handgestrickten
// fetch-Wrapper zu pflegen. Signaturen bleiben identisch, damit Konsumenten
// (EditorPane, discovery.svelte.ts, App.svelte) unverändert bleiben.

import { HrpcRestClient } from 'hrpc-client';
import type { DiscoveryInfo, HrpcMultiRequest, HrpcRequest, HrpcResponse } from 'hrpc-client';

// Verbindung ist modul-lokal mutierbar. `baseUrl "/"` liefert relative Same-Origin-
// Pfade ("/api/hrpc/..."), sodass die DevUI hinter jedem Proxy/Port läuft, ohne
// window.location zu bemühen — der Standalone-Build überschreibt baseUrl später
// mit "" (User muss Ziel eingeben). `client` ist statelos (nur Konfiguration);
// `rebuild()` instanziiert ihn neu, wenn sich baseUrl/apiPath/bearer ändern. Die
// drei Fassaden-Funktionen schließen über die Modul-Variable und sehen somit
// immer den aktuellen Client.
let baseUrl = '/';
let apiPath = 'api/hrpc';
let bearer: string | undefined;
let client = new HrpcRestClient(baseUrl, { apiPath, bearer });

/** Client neu instanziieren, nachdem baseUrl/apiPath/bearer geändert wurden. */
function rebuild(): void {
  client = new HrpcRestClient(baseUrl, { apiPath, bearer: bearer || undefined });
}

/**
 * Setzt den Bearer-Token für alle nachfolgenden Aufrufe (Discovery, Single,
 * Batch). Leerstring/undefined → kein Authorization-Header (Status quo).
 * Wird vom Auth-State (auth.svelte.ts) gerufen; einweg-Abhängigkeit (kein Zyklus,
 * client.ts importiert keinen State).
 */
export function setBearer(token: string): void {
  bearer = token || undefined;
  rebuild();
}

/**
 * Setzt die Ziel-Verbindung (baseUrl + apiPath) für alle nachfolgenden Aufrufe.
 * Ermöglicht es der (Standalone-)DevUI, auf einen beliebigen Hrpc-Server zu
 * zeigen statt nur auf Same-Origin `/api/hrpc`. Wird vom Endpoint-State
 * (endpoint.svelte.ts) gerufen. `url=""` → Client baut relative URLs (kein Host).
 */
export function setEndpoint(url: string, path: string): void {
  baseUrl = url || '/';
  apiPath = path || 'api/hrpc';
  rebuild();
}

export async function fetchDiscovery(): Promise<DiscoveryInfo> {
  return client.discover();
}

export async function executeRequest(request: HrpcRequest): Promise<HrpcResponse> {
  return client.call(request);
}

export async function executeBatch(request: HrpcMultiRequest): Promise<HrpcResponse[]> {
  return client.callBatch(request.requests, request.mode);
}