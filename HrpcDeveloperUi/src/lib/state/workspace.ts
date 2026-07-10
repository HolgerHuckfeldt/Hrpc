// Workspace-Export/-Import: fasst Verbindung + offene Tabs in einer JSON-Datei
// zusammen, damit man die DevUI-Konfiguration (z. B. für einen Hrpc-Server) teilen
// oder als echte "Developer-UI"-Sitzung ablegen kann.
//
// Wichtig: der Bearer-Token wird bewusst NICHT exportiert (Secret-Leak-Schutz).
// Er bleibt separat in hrpc-bearer und greift nach Import weiterhin (unangetastet).

import type { Tab } from './tabs.svelte';
import { tabState } from './tabs.svelte';
import { endpointState } from './endpoint.svelte';

export interface WorkspaceConnection {
  baseUrl: string;
  apiPath: string;
}

export interface Workspace {
  version: 1;
  connection: WorkspaceConnection;
  tabs: Tab[];
  activeTabId: string | null;
}

const FILENAME = 'hrpc-workspace.json';

/** Baut den aktuellen Workspace (ohne Bearer). */
export function buildWorkspace(): Workspace {
  return {
    version: 1,
    connection: { baseUrl: endpointState.baseUrl, apiPath: endpointState.apiPath },
    tabs: tabState.tabs,
    activeTabId: tabState.activeTabId,
  };
}

/** Löst einen Browser-Download der Workspace-Datei aus. */
export function exportWorkspaceFile(): void {
  const json = JSON.stringify(buildWorkspace(), null, 2);
  const blob = new Blob([json], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = FILENAME;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

/**
 * Importiert einen Workspace aus JSON-Text: parst, prüft die Version, setzt die
 * Verbindung (Endpoint-State + Fassade) und stellt die Tabs wieder her. Bearer
 * bleibt unangetastet. Wirft bei ungültigem Input (Aufrufer fängt).
 */
export function importWorkspaceFromText(json: string): void {
  const parsed = JSON.parse(json) as Partial<Workspace>;
  if (!parsed || typeof parsed !== 'object') {
    throw new Error('Kein gültiges Workspace-JSON.');
  }
  if (parsed.version !== 1) {
    throw new Error(`Nicht unterstützte Workspace-Version: ${parsed.version ?? 'fehlt'}.`);
  }
  if (!Array.isArray(parsed.tabs)) {
    throw new Error('Workspace enthält keine Tabs.');
  }
  const conn = parsed.connection ?? { baseUrl: '/', apiPath: 'api/hrpc' };
  endpointState.set(conn.baseUrl ?? '/', conn.apiPath ?? 'api/hrpc');
  tabState.restoreFromWorkspace(parsed.tabs as Tab[], parsed.activeTabId ?? null);
}