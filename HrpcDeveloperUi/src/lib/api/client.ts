import type { DiscoveryInfo, HrpcRequest, HrpcMultiRequest, HrpcResponse } from '../types/discovery';

const API_ROOT = '/api/hrpc';

export async function fetchDiscovery(): Promise<DiscoveryInfo> {
  const res = await fetch(`${API_ROOT}/discovery`);
  if (!res.ok) {
    throw new Error(`Discovery failed: ${res.status} ${res.statusText}`);
  }
  return res.json();
}

export async function executeRequest(request: HrpcRequest): Promise<HrpcResponse> {
  const res = await fetch(`${API_ROOT}/json`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`HTTP ${res.status}: ${text || res.statusText}`);
  }
  return res.json();
}

export async function executeBatch(request: HrpcMultiRequest): Promise<HrpcResponse[]> {
  const res = await fetch(`${API_ROOT}/json/multi`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });
  if (!res.ok) {
    throw new Error(`Batch request failed: ${res.status} ${res.statusText}`);
  }
  return res.json();
}
