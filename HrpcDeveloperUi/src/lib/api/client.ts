// Dünne Fassade über dem offiziellen Hrpc TS-Client (hrpc-client).
// Die DevUI dogfooded hier ihren eigenen Client statt einen handgestrickten
// fetch-Wrapper zu pflegen. Signaturen bleiben identisch, damit Konsumenten
// (EditorPane, discovery.svelte.ts, App.svelte) unverändert bleiben.

import { HrpcRestClient } from 'hrpc-client';
import type { DiscoveryInfo, HrpcMultiRequest, HrpcRequest, HrpcResponse } from 'hrpc-client';

// baseUrl "/" liefert relative Same-Origin-Pfade ("/api/hrpc/..."), sodass die
// DevUI hinter jedem Proxy/Port läuft, ohne window.location zu bemühen.
const client = new HrpcRestClient('/', { apiPath: 'api/hrpc' });

export async function fetchDiscovery(): Promise<DiscoveryInfo> {
  return client.discover();
}

export async function executeRequest(request: HrpcRequest): Promise<HrpcResponse> {
  return client.call(request);
}

export async function executeBatch(request: HrpcMultiRequest): Promise<HrpcResponse[]> {
  return client.callBatch(request.requests, request.mode);
}