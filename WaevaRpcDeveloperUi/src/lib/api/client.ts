// Dünne Fassade über dem offiziellen WaevaRpc TS-Client (waeva-rpc-client).
// Die DevUI dogfooded hier ihren eigenen Client statt einen handgestrickten
// fetch-Wrapper zu pflegen. Signaturen bleiben identisch, damit Konsumenten
// (EditorPane, discovery.svelte.ts, App.svelte) unverändert bleiben.

import { WaevaRpcRestClient } from 'waeva-rpc-client';
import type { DiscoveryInfo, WaevaRpcMultiRequest, WaevaRpcRequest, WaevaRpcResponse } from 'waeva-rpc-client';

// Verbindung ist modul-lokal mutierbar. `baseUrl "/"` liefert relative Same-Origin-
// Pfade ("/api/waeva/..."), sodass die DevUI hinter jedem Proxy/Port läuft, ohne
// window.location zu bemühen — der Standalone-Build überschreibt baseUrl später
// mit "" (User muss Ziel eingeben). `client` ist statelos (nur Konfiguration);
// `rebuild()` instanziiert ihn neu, wenn sich baseUrl/apiPath/bearer ändern. Die
// drei Fassaden-Funktionen schließen über die Modul-Variable und sehen somit
// immer den aktuellen Client.
let baseUrl = '/';
let apiPath = 'api/waeva';
let bearer: string | undefined;
let client = new WaevaRpcRestClient(baseUrl, { apiPath, bearer });

/** Client neu instanziieren, nachdem baseUrl/apiPath/bearer geändert wurden. */
function rebuild(): void {
  client = new WaevaRpcRestClient(baseUrl, { apiPath, bearer: bearer || undefined });
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
 * Ermöglicht es der (Standalone-)DevUI, auf einen beliebigen WaevaRpc-Server zu
 * zeigen statt nur auf Same-Origin `/api/waeva`. Wird vom Endpoint-State
 * (endpoint.svelte.ts) gerufen. `url=""` → Client baut relative URLs (kein Host).
 */
export function setEndpoint(url: string, path: string): void {
  baseUrl = url || '/';
  apiPath = path || 'api/waeva';
  rebuild();
}

export async function fetchDiscovery(): Promise<DiscoveryInfo> {
  return client.discover();
}

export async function executeRequest(request: WaevaRpcRequest): Promise<WaevaRpcResponse> {
  return client.call(request);
}

export async function executeBatch(request: WaevaRpcMultiRequest): Promise<WaevaRpcResponse[]> {
  return client.callBatch(request.requests, request.mode);
}