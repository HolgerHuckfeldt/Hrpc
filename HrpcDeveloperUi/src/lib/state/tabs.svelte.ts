import type { ControllerMeta, MethodMeta, ParameterMeta, HrpcParameter } from 'hrpc-client';
import { formatJson } from '../utils/json';

export type TabType = 'request' | 'codegen';

export interface Tab {
  id: string;
  type: TabType;
  title: string;
  controller: ControllerMeta | null;
  method: MethodMeta | null;
  params: (ParameterMeta & { value: unknown })[];
  requestText: string;
  responseText: string;
  status: string;
  respIdText: string;
  duration: string;
  log: string;
}

let counter = 0;

function generateId(): string {
  return `${Date.now()}-${Math.random().toString(16).slice(2, 6)}-${counter++}`;
}

function defaultValueForType(typeName: string): unknown {
  const t = typeName.toLowerCase();
  if (t === 'bool' || t === 'boolean') return false;
  if (t === 'int' || t === 'int32' || t === 'int64' || t === 'long' || t === 'number' || t === 'double' || t === 'float' || t === 'decimal' || t === 'single') return 0;
  if (t === 'string') return '';
  return null;
}

function defaultValueForParam(param: ParameterMeta): unknown {
  if (param.typeDefinition?.properties?.length) {
    const obj: Record<string, unknown> = {};
    for (const prop of param.typeDefinition.properties) {
      obj[prop.propertyName] = defaultValueForType(prop.propertyType);
    }
    return obj;
  }
  return defaultValueForType(param.parameterType);
}

function serializeValueByType(value: unknown, type: string, typeDefinition?: { properties?: { propertyName: string; propertyType: string }[] } | null): string {
  if (typeDefinition?.properties?.length) {
    let v = value;
    if (typeof v === 'string') {
      try { v = JSON.parse(v); } catch { v = {}; }
    }
    if (v === null || typeof v !== 'object') v = {};
    const obj = v as Record<string, unknown>;
    // Ensure all properties exist with correct types
    for (const prop of typeDefinition.properties) {
      if (!(prop.propertyName in obj) || obj[prop.propertyName] === '') {
        obj[prop.propertyName] = defaultValueForType(prop.propertyType);
      }
    }
    return JSON.stringify(obj);
  }
  const t = (type || '').toLowerCase();
  if (t === 'string') return JSON.stringify(value ?? '');
  if (t === 'bool' || t === 'boolean') return JSON.stringify(value === '' ? false : !!value);
  if (t === 'int' || t === 'int32' || t === 'int64' || t === 'long' || t === 'number' || t === 'double' || t === 'float' || t === 'decimal' || t === 'single') {
    const n = value === '' ? 0 : Number(value);
    return JSON.stringify(Number.isNaN(n) ? 0 : n);
  }
  return JSON.stringify(value ?? null);
}

function buildDefaultPayload(params: (ParameterMeta & { value: unknown })[]): HrpcParameter[] {
  return params.map((p) => ({
    parameterName: p.parameterName,
    data: serializeValueByType(p.value, p.parameterType, p.typeDefinition),
  }));
}

class TabState {
  tabs = $state<Tab[]>([]);
  activeTabId = $state<string | null>(null);

  get activeTab(): Tab | undefined {
    return this.tabs.find((t) => t.id === this.activeTabId);
  }

  createTab(partial: Partial<Tab> = {}): Tab {
    const tab: Tab = {
      type: 'request',
      id: generateId(),
      title: 'New request',
      controller: null,
      method: null,
      params: [],
      requestText: '[]',
      responseText: '{}',
      status: '-',
      respIdText: '-',
      duration: '-- ms',
      log: '',
      ...partial,
    };
    this.tabs.push(tab);
    this.activeTabId = tab.id;
    return tab;
  }

  createCodegenTab(): Tab {
    // Reuse existing codegen tab if open
    const existing = this.tabs.find((t) => t.type === 'codegen');
    if (existing) {
      this.activeTabId = existing.id;
      return existing;
    }
    const tab: Tab = {
      type: 'codegen',
      id: generateId(),
      title: 'Codegen',
      controller: null,
      method: null,
      params: [],
      requestText: '',
      responseText: '',
      status: '',
      respIdText: '',
      duration: '',
      log: '',
    };
    this.tabs.push(tab);
    this.activeTabId = tab.id;
    return tab;
  }

  closeTab(id: string) {
    if (this.tabs.length <= 1) return;
    const idx = this.tabs.findIndex((t) => t.id === id);
    if (idx === -1) return;
    this.tabs.splice(idx, 1);
    if (this.activeTabId === id) {
      const fallback = this.tabs[Math.max(0, idx - 1)];
      this.activeTabId = fallback.id;
    }
  }

  switchTab(id: string) {
    this.activeTabId = id;
  }

  openMethodTab(controller: ControllerMeta, method: MethodMeta) {
    const existing = this.tabs.find(
      (t) => t.controller?.name === controller.name && t.method?.methodName === method.methodName
    );
    if (existing) {
      this.activeTabId = existing.id;
      return existing;
    }
    const params = method.parameters.map((p) => ({ ...p, value: defaultValueForParam(p) }));
    const payload = formatJson(buildDefaultPayload(params));
    return this.createTab({
      title: `${controller.name}.${method.methodName}`,
      controller,
      method,
      params,
      requestText: payload,
    });
  }

  syncRequestFromParams(tab: Tab) {
    const payload = buildDefaultPayload(tab.params);
    tab.requestText = formatJson(payload);
  }

  syncParamsFromEditor(tab: Tab, editorText: string) {
    try {
      const arr = JSON.parse(editorText);
      if (Array.isArray(arr)) {
        for (const p of tab.params) {
          const match = arr.find((x: HrpcParameter) => x.parameterName === p.parameterName);
          if (match) {
            try {
              p.value = JSON.parse(match.data);
            } catch {
              p.value = match.data;
            }
          }
        }
      }
      tab.requestText = editorText;
    } catch {
      /* ignore parse errors */
    }
  }
}

export const tabState = new TabState();
