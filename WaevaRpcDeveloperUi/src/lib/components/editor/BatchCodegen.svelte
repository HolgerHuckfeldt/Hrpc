<script lang="ts">
  // Code-Generator für einen konfigurierten Batch — erzeugt kopierbaren
  // TypeScript-/C#-/JSON-Code für die im BatchEditor zusammengestellten Zeilen.
  //
  // Vorbild: DependencyBuilderPage (generateTs/Cs/Json). Im Gegensatz zum
  // Dependency Builder (Serial-locked) nutzt dieser Generator den tatsächlichen
  // Modus (Parallel/Serial) der übergebenen `mode`-Prop. Null-`dependencyMapping`
  // wird weggelassen, damit kein null im generierten Code steht (saubere Wire-Form).

  import { onDestroy } from 'svelte';
  import { ExecutionMode, type WaevaRpcRequest } from 'waeva-rpc-client';

  let {
    requests = [],
    mode = ExecutionMode.Parallel,
  }: {
    requests: WaevaRpcRequest[];
    mode: ExecutionMode;
  } = $props();

  let activeLang = $state<'ts' | 'cs' | 'json'>('ts');
  let copied = $state('');
  let copyTimer = $state<ReturnType<typeof setTimeout> | null>(null);

  onDestroy(() => {
    if (copyTimer) clearTimeout(copyTimer);
  });

  const MODE_NAMES: Record<number, string> = {
    [ExecutionMode.Parallel]: 'Parallel',
    [ExecutionMode.Serial]: 'Serial',
  };

  /** Wire-Form eines Requests: null/leeres dependencyMapping wird weggelassen
   *  (kein null im generierten Code). Spiegelt DependencyBuilderPage.toWireRequest. */
  function toWire(req: WaevaRpcRequest): WaevaRpcRequest {
    if (!req.dependencyMapping || Object.keys(req.dependencyMapping).length === 0) {
      const { dependencyMapping: _omit, ...rest } = req;
      return rest as WaevaRpcRequest;
    }
    return req;
  }

  let wireRequests = $derived.by(() => requests.map(toWire));

  const emptyMsg = '// Noch keine Batch-Zeilen — im Editor „+ Add" klicken.';

  // --- TypeScript ---------------------------------------------------------------

  function generateTs(): string {
    if (requests.length === 0) return emptyMsg;
    const modeName = MODE_NAMES[mode] ?? 'Serial';
    const reqObjects = wireRequests
      .map((r) => '    ' + JSON.stringify(r, null, 2).replace(/\n/g, '\n    '))
      .join(',\n');
    return `import { ExecutionMode, type WaevaRpcMultiRequest } from "waeva-rpc-client";

// Batch-Ausführung (Mode: ${modeName}${mode === ExecutionMode.Serial ? ' — @alias-Auflösung aktiv' : ''})
const batch: WaevaRpcMultiRequest = {
  mode: ExecutionMode.${modeName},
  requests: [
${reqObjects}
  ],
};

const results = await rest.callBatch(batch.requests, batch.mode);`;
  }

  // --- C# -----------------------------------------------------------------------

  function csEscape(s: string): string {
    return s.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
  }

  function generateCs(): string {
    if (requests.length === 0) return emptyMsg;
    const modeName = MODE_NAMES[mode] ?? 'Serial';
    const reqBlocks = wireRequests
      .map((r) => {
        const mappingLine = r.dependencyMapping
          ? `\n            DependencyMapping = new Dictionary<string, string> { ${Object.entries(r.dependencyMapping)
              .map(([k, v]) => `["${k}"] = "${v}"`)
              .join(', ')} },`
          : '';
        return `        new WaevaRpcRequest
        {
            Controller = "${r.controller}",
            Method = "${r.method}",
            Id = "${r.id ?? ''}",
            StringData = "${csEscape(r.stringData ?? '[]')}",${mappingLine}
        }`;
      })
      .join(',\n');
    return `using WaevaRpcClient.WaevaRpc;
using WaevaRpcCommon.Models;
using System.Collections.Generic;

// Batch-Ausführung (Mode: ${modeName}${mode === ExecutionMode.Serial ? ' — @alias-Auflösung aktiv' : ''})
var multi = new WaevaRpcMultiRequest
{
    Mode = ExecutionMode.${modeName},
    Requests = new List<WaevaRpcRequest>
    {
${reqBlocks}
    }
};

var responses = await client.Call(multi);`;
  }

  // --- JSON ---------------------------------------------------------------------

  function generateJson(): string {
    if (requests.length === 0) return emptyMsg;
    const wire = {
      mode, // ExecutionMode: Parallel=0, Serial=1
      requests: wireRequests,
    };
    return JSON.stringify(wire, null, 2);
  }

  let code = $derived(
    activeLang === 'ts' ? generateTs() : activeLang === 'cs' ? generateCs() : generateJson(),
  );

  async function copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(code);
      copied = activeLang;
      if (copyTimer) clearTimeout(copyTimer);
      copyTimer = setTimeout(() => {
        copied = '';
        copyTimer = null;
      }, 1500);
    } catch {
      /* ignore */
    }
  }
</script>

<div class="batch-codegen">
  <div class="codegen-header">
    <span class="block-label">Code</span>
    <div class="lang-tabs">
      <button class="ghost small" class:active={activeLang === 'ts'} onclick={() => (activeLang = 'ts')}>TypeScript</button>
      <button class="ghost small" class:active={activeLang === 'cs'} onclick={() => (activeLang = 'cs')}>C#</button>
      <button class="ghost small" class:active={activeLang === 'json'} onclick={() => (activeLang = 'json')}>JSON</button>
    </div>
    <button class="primary small" onclick={copy}>
      {copied === activeLang ? 'Kopiert!' : 'Code kopieren'}
    </button>
  </div>
  <pre class="code codegen-output"><code>{code}</code></pre>
</div>

<style>
  .batch-codegen {
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    background: var(--bg-elevated);
    padding: 8px;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .codegen-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    flex-shrink: 0;
  }
  .block-label {
    font-size: 0.72rem;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    color: var(--text-muted);
  }
  .lang-tabs {
    display: flex;
    gap: 2px;
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    overflow: hidden;
    margin-left: auto;
  }
  .lang-tabs button {
    border: none;
    border-radius: 0;
    padding: 3px 10px;
    font-size: 0.8rem;
  }
  .lang-tabs button.active {
    background: var(--accent-secondary);
    color: #fff;
  }
  .codegen-output {
    margin: 0;
    white-space: pre-wrap;
    word-break: break-all;
    padding: 8px;
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    background: var(--code-bg);
    color: var(--code-text);
    font-family: var(--font-mono);
    font-size: 0.8rem;
    line-height: 1.6;
    max-height: 300px;
    overflow-y: auto;
  }
  .codegen-output code {
    font-family: var(--font-mono);
  }
</style>