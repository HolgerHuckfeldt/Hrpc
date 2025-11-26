(() => {
  const apiRoot = "/api/hrpc";

  const el = (id) => document.getElementById(id);
  const tree = el("controller-tree");
  const typesTree = el("types-tree");
  const statusLine = el("discovery-status");
  const selectionLabel = el("selection-label");
  const paramSummary = el("param-summary");
  const durationLabel = el("duration");
  const requestEditor = el("request-editor");
  const paramList = el("param-list");
  const respStatus = el("resp-status");
  const respId = el("resp-id");
  const responseView = el("response-view");
  const logView = el("log-view");
  const statusPill = el("status-pill");
  const tsSnippet = el("ts-snippet");
  const csSnippet = el("cs-snippet");
  const tabBar = el("tab-bar");
  const tabNew = el("tab-new");

  const state = {
    discovery: null,
    tabs: [],
    activeTabId: null,
  };

  const formatJson = (value) => {
    try {
      const parsed = typeof value === "string" ? JSON.parse(value) : value;
      return JSON.stringify(parsed, null, 2);
    } catch (e) {
      return value;
    }
  };

  const setStatus = (text) => statusLine.textContent = text;
  const setRunStatus = (text) => statusPill.textContent = text;

  const getActiveTab = () => state.tabs.find((t) => t.id === state.activeTabId);

  const buildDefaultPayload = (params) => params.map((p) => ({
    parameterName: p.parameterName,
    data: serializeValueByType(defaultValueForParam(p), p.parameterType, p.typeDefinition)
  }));

  const defaultValueForParam = (param) => {
    if (param.typeDefinition?.properties?.length) {
      return Object.fromEntries(param.typeDefinition.properties.map(prop => [prop.propertyName, ""]));
    }
    const t = (param.parameterType || "").toLowerCase();
    if (t === "bool" || t === "boolean") return false;
    if (t === "number" || t === "int" || t === "double") return 0;
    if (t === "string") return "";
    return null;
  };

  const serializeValueByType = (value, type, typeDefinition) => {
    if (typeDefinition?.properties?.length) {
      let v = value;
      if (typeof v === "string") {
        try { v = JSON.parse(v); } catch { v = {}; }
      }
      if (v === null || typeof v !== "object") v = {};
      // ensure all properties exist
      typeDefinition.properties.forEach(prop => {
        if (!(prop.propertyName in v)) v[prop.propertyName] = "";
      });
      return JSON.stringify(v);
    }
    const t = (type || "").toLowerCase();
    if (t === "string") return JSON.stringify(value ?? "");
    if (t === "bool" || t === "boolean") return JSON.stringify(value === "" ? false : !!value);
    if (t === "number" || t === "int" || t === "double") {
      const n = value === "" ? 0 : Number(value);
      return JSON.stringify(Number.isNaN(n) ? 0 : n);
    }
    return JSON.stringify(value ?? null);
  };

  const createTab = ({ title = "New request", controller = null, method = null, params = [], requestText = "[]", responseText = "{}", status = "-", respIdText = "-", duration = "-- ms", log = "" }) => {
    const id = `${Date.now()}-${Math.random().toString(16).slice(2, 6)}`;
    const tab = { id, title, controller, method, params, requestText, responseText, status, respIdText, duration, log };
    state.tabs.push(tab);
    state.activeTabId = id;
    renderTabs();
    hydrateUI();
    return tab;
  };

  const closeTab = (id) => {
    if (state.tabs.length <= 1) return;
    const idx = state.tabs.findIndex((t) => t.id === id);
    if (idx === -1) return;
    state.tabs.splice(idx, 1);
    if (state.activeTabId === id) {
      const fallback = state.tabs[Math.max(0, idx - 1)];
      state.activeTabId = fallback.id;
    }
    renderTabs();
    hydrateUI();
  };

  const renderTabs = () => {
    tabBar.innerHTML = "";
    state.tabs.forEach((t) => {
      const tab = document.createElement("div");
      tab.className = `tab ${t.id === state.activeTabId ? "active" : ""}`;
      tab.dataset.id = t.id;
      const title = document.createElement("span");
      title.className = "tab-title";
      title.textContent = t.title;
      const close = document.createElement("span");
      close.className = "close";
      close.textContent = "×";
      close.onclick = (e) => { e.stopPropagation(); closeTab(t.id); };
      tab.onclick = () => switchTab(t.id);
      tab.appendChild(title);
      if (state.tabs.length > 1) tab.appendChild(close);
      tabBar.appendChild(tab);
    });
    tabBar.appendChild(tabNew);
  };

  const switchTab = (id) => {
    persistActiveTab();
    state.activeTabId = id;
    renderTabs();
    hydrateUI();
  };

  const persistActiveTab = () => {
    const tab = getActiveTab();
    if (!tab) return;
    tab.requestText = requestEditor.value;
    tab.responseText = responseView.textContent;
    tab.log = logView.textContent;
    tab.status = respStatus.textContent;
    tab.respIdText = respId.textContent;
    tab.duration = durationLabel.textContent;
  };

  const updateSelectionLabel = (tab) => {
    if (!tab?.controller || !tab?.method) {
      selectionLabel.textContent = "No method selected";
      paramSummary.textContent = "0 params";
      return;
    }
    selectionLabel.textContent = `${tab.controller.name}.${tab.method.methodName}`;
    paramSummary.textContent = `${tab.params.length} params`;
  };

  const inferValue = (v, type) => {
    if (typeof v === "string") {
      const t = type?.toLowerCase();
      if (t === "number" || t === "int" || t === "double") {
        const n = Number(v); return Number.isNaN(n) ? v : n;
      }
      if (t === "bool" || t === "boolean") {
        if (v.toLowerCase() === "true") return true;
        if (v.toLowerCase() === "false") return false;
      }
    }
    return v;
  };

  const buildParamCard = (param, tab) => {
    const card = document.createElement("div");
    card.className = "param-card";
    const name = document.createElement("div");
    name.className = "param-name";
    name.textContent = param.parameterName;
    const type = document.createElement("div");
    type.className = "param-type";
    type.textContent = param.parameterType;
    card.appendChild(name);
    card.appendChild(type);

    if (param.typeDefinition?.properties?.length) {
      const ta = document.createElement("textarea");
      ta.className = "code";
      const skeleton = defaultValueForParam(param);
      const current = (param.value && typeof param.value === "object") ? param.value : skeleton;
      ta.value = JSON.stringify(current, null, 2);
      ta.oninput = (e) => {
        try {
          param.value = JSON.parse(e.target.value);
        } catch {
          param.value = e.target.value;
        }
        syncRequestFromParams(tab);
      };
      card.appendChild(ta);
    } else {
      const input = document.createElement("input");
      input.placeholder = param.parameterName;
      input.value = param.value ?? "";
      input.oninput = (e) => {
        param.value = inferValue(e.target.value, param.parameterType);
        syncRequestFromParams(tab);
      };
      card.appendChild(input);
    }
    return card;
  };

  const renderParams = (tab) => {
    paramList.innerHTML = "";
    (tab.params || []).forEach((p) => paramList.appendChild(buildParamCard(p, tab)));
    updateSelectionLabel(tab);
  };

  const syncRequestFromParams = (tab) => {
    const payload = (tab.params || []).map((p) => ({
      parameterName: p.parameterName,
      data: serializeValueByType(p.value, p.parameterType, p.typeDefinition),
    }));
    tab.requestText = formatJson(payload);
    requestEditor.value = tab.requestText;
  };

  const syncParamsFromEditor = (tab) => {
    try {
      const arr = JSON.parse(requestEditor.value);
      if (Array.isArray(arr)) {
        (tab.params || []).forEach((p) => {
          const match = arr.find(x => x.parameterName === p.parameterName);
          if (match) {
            try {
              const parsed = JSON.parse(match.data);
              p.value = parsed;
            } catch {
              p.value = match.data;
            }
          }
        });
        renderParams(tab);
      }
      tab.requestText = requestEditor.value;
    } catch { /* ignore */ }
  };

  const renderTree = () => {
    tree.innerHTML = "";
    if (!state.discovery) return;
    state.discovery.controllers.forEach((c) => {
      const cNode = document.createElement("div");
      cNode.className = "node controller";
      cNode.textContent = c.name;
      tree.appendChild(cNode);
      c.methods.forEach((m) => {
        const mNode = document.createElement("div");
        mNode.className = "node method";
        mNode.textContent = `${m.methodName}(${m.parameters.map((p) => p.parameterName).join(", ")})`;
        mNode.onclick = () => openMethodTab(c, m);
        tree.appendChild(mNode);
      });
    });
  };

  const renderTypes = () => {
    typesTree.innerHTML = "";
    if (!state.discovery) return;
    Object.keys(state.discovery.types || {}).forEach((t) => {
      const tNode = document.createElement("div");
      tNode.className = "node type";
      tNode.textContent = t;
      typesTree.appendChild(tNode);
      (state.discovery.types[t].properties || []).forEach((p) => {
        const pNode = document.createElement("div");
        pNode.className = "node type";
        pNode.style.paddingLeft = "18px";
        pNode.textContent = `${p.propertyName}: ${p.propertyType}`;
        typesTree.appendChild(pNode);
      });
    });
  };

  const openMethodTab = (controller, method) => {
    persistActiveTab();
    const existing = state.tabs.find((t) => t.controller?.name === controller.name && t.method?.methodName === method.methodName);
    if (existing) {
      switchTab(existing.id);
      return;
    }
      const params = method.parameters.map((p) => ({ ...p, value: defaultValueForParam(p) }));
      const payload = formatJson(buildDefaultPayload(params));
      createTab({
        title: `${controller.name}.${method.methodName}`,
        controller,
        method,
      params,
      requestText: payload,
      responseText: "{}",
      status: "-",
      respIdText: "-",
      duration: "-- ms",
      log: ""
    });
  };

  const updateCodegen = (tab) => {
    if (!tab?.controller || !tab?.method) {
      tsSnippet.textContent = "// select a method";
      csSnippet.textContent = "// select a method";
      return;
    }
    const ctrl = tab.controller.name;
    const m = tab.method.methodName;
    const args = (tab.params || []).map((p) => {
      const normalized = serializeValueByType(p.value, p.parameterType, p.typeDefinition); // already JSON string
      return `  { parameterName: "${p.parameterName}", data: ${normalized} }`;
    }).join(",\n");
    tsSnippet.textContent =
`import { hrpc } from "./hrpc-client";

const res = await hrpc("${ctrl}", "${m}", [
${args}
]);
console.log(res.code, res.data);`;

    csSnippet.textContent =
`using var client = new HrpcClient("/api/hrpc");
var result = await client.CallAsync("${ctrl}", "${m}", new [] {
${(tab.params || []).map((p, idx) => {
      const normalized = serializeValueByType(p.value, p.parameterType, p.typeDefinition); // JSON string
      return `    new HrpcCallArg("${p.parameterName}", ${normalized})`;
    }).join(",\n")}
});
Console.WriteLine(result.Code);`;
  };

  const hydrateUI = () => {
    const tab = getActiveTab();
    if (!tab) return;
    requestEditor.value = tab.requestText ?? "";
    responseView.textContent = tab.responseText ?? "{}";
    logView.textContent = tab.log ?? "";
    respStatus.textContent = tab.status ?? "-";
    respId.textContent = tab.respIdText ?? "-";
    durationLabel.textContent = tab.duration ?? "-- ms";
    renderParams(tab);
    updateCodegen(tab);
  };

  const runCall = async () => {
    const tab = getActiveTab();
    if (!tab || !tab.controller || !tab.method) {
      logView.textContent = "Select a method first.";
      return;
    }
    setRunStatus("Running...");
    const start = performance.now();
    try {
      syncParamsFromEditor(tab);
      const body = {
        controller: tab.controller.name,
        method: tab.method.methodName,
        stringData: tab.requestText,
        id: `${tab.controller.name}.${tab.method.methodName}`,
        dependencyMapping: null,
        dataPath: "$"
      };
      const res = await fetch(`${apiRoot}/json`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
      });
      const json = await res.json();
      tab.duration = `${Math.round(performance.now() - start)} ms`;
      tab.status = json.code ?? res.status;
      tab.respIdText = json.id ?? "-";
      tab.responseText = formatJson(json.data ?? json);
      tab.log = res.ok ? "" : `HTTP ${res.status}`;
      hydrateUI();
      setRunStatus("Done");
    } catch (err) {
      tab.duration = `${Math.round(performance.now() - start)} ms`;
      tab.status = "Error";
      tab.log = err?.message ?? String(err);
      hydrateUI();
      setRunStatus("Error");
    }
  };

  const formatEditor = () => {
    const tab = getActiveTab();
    if (!tab) return;
    tab.requestText = formatJson(requestEditor.value);
    requestEditor.value = tab.requestText;
  };

  const resetEditor = () => {
    const tab = getActiveTab();
    if (!tab) return;
    if (tab.controller && tab.method) {
      tab.params = tab.method.parameters.map((p) => ({ ...p, value: defaultValueForParam(p) }));
      syncRequestFromParams(tab);
    } else {
      tab.requestText = "[]";
      requestEditor.value = "[]";
      tab.params = [];
      renderParams(tab);
    }
    tab.responseText = "{}";
    tab.log = "";
    tab.status = "-";
    tab.respIdText = "-";
    tab.duration = "-- ms";
    hydrateUI();
  };

  const fetchDiscovery = async () => {
    setStatus("Loading discovery...");
    try {
      const res = await fetch(`${apiRoot}/discovery`);
      const json = await res.json();
      state.discovery = json;
      renderTree();
      renderTypes();
      setStatus("Discovery loaded");
    } catch (err) {
      setStatus("Discovery failed");
      logView.textContent = err?.message ?? String(err);
    }
  };

  const copyToClipboard = async (txt) => {
    try { await navigator.clipboard.writeText(txt); setRunStatus("Copied"); setTimeout(() => setRunStatus("Idle"), 800); }
    catch { /* ignore */ }
  };

  const setupSplitters = () => {
    const explorer = document.getElementById("pane-explorer");
    const editor = document.getElementById("pane-editor");
    const result = document.getElementById("pane-result");
    const leftSplit = document.getElementById("split-left");
    const rightSplit = document.getElementById("split-right");

    const clamp = (val, min, max) => Math.max(min, Math.min(max, val));

    const drag = (splitter, onDragStart) => {
      let startX = 0;
      let sizes = null;
      const onMove = (e) => {
        e.preventDefault();
        if (!sizes) return;
        const dx = e.clientX - startX;
        onDragStart(dx, sizes);
      };
      const onUp = () => {
        window.removeEventListener("mousemove", onMove);
        window.removeEventListener("mouseup", onUp);
      };
      splitter.addEventListener("mousedown", (e) => {
        e.preventDefault();
        startX = e.clientX;
        sizes = {
          l: explorer.getBoundingClientRect().width,
          m: editor.getBoundingClientRect().width,
          r: result.getBoundingClientRect().width
        };
        window.addEventListener("mousemove", onMove);
        window.addEventListener("mouseup", onUp);
      });
    };

    drag(leftSplit, (dx, s) => {
      const total = s.l + s.m + s.r;
      const newL = clamp(s.l + dx, 200, total - s.r - 320);
      const newM = clamp(total - s.r - newL, 320, total - s.r - 200);
      explorer.style.width = `${newL}px`;
      editor.style.width = `${newM}px`;
    });

    drag(rightSplit, (dx, s) => {
      const total = s.l + s.m + s.r;
      const newM = clamp(s.m + dx, 320, total - s.l - 260);
      const newR = clamp(total - newM - s.l, 260, total - newM - 200);
      editor.style.width = `${newM}px`;
      result.style.width = `${newR}px`;
    });
  };

  // wire actions
  el("run").onclick = runCall;
  el("format").onclick = formatEditor;
  el("reset").onclick = resetEditor;
  el("refresh-discovery").onclick = fetchDiscovery;
  el("copy-ts").onclick = () => copyToClipboard(tsSnippet.textContent);
  el("copy-cs").onclick = () => copyToClipboard(csSnippet.textContent);
  tabNew.onclick = () => createTab({ title: "New request", requestText: "[]", params: [] });
  requestEditor.addEventListener("blur", () => { const tab = getActiveTab(); if (tab) syncParamsFromEditor(tab); });

  // bootstrap
  createTab({ title: "Welcome", requestText: "[]", params: [] });
  renderTabs();
  hydrateUI();
  fetchDiscovery();
  setupSplitters();
})();
