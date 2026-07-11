<script lang="ts">
  import { onDestroy } from 'svelte';
  import type { Tab } from '../../state/tabs.svelte.ts';

  let { tab }: { tab: Tab } = $props();

  let tsSnippet = $derived.by(() => {
    if (!tab.controller || !tab.method) return '// select a method';
    const ctrl = tab.controller.name;
    const m = tab.method.methodName;
    const args = tab.params
      .map((p) => {
        const val = typeof p.value === 'object' ? JSON.stringify(p.value) : JSON.stringify(p.value ?? '');
        return `  { parameterName: "${p.parameterName}", data: ${val} }`;
      })
      .join(',\n');
    return `import { waeva-rpc } from "./waeva-rpc-client";

const res = await waeva-rpc("${ctrl}", "${m}", [
${args}
]);
console.log(res.code, res.data);`;
  });

  let csSnippet = $derived.by(() => {
    if (!tab.controller || !tab.method) return '// select a method';
    const ctrl = tab.controller.name;
    const m = tab.method.methodName;
    const args = tab.params
      .map((p) => {
        const val = typeof p.value === 'object' ? JSON.stringify(p.value) : JSON.stringify(p.value ?? '');
        return `    new WaevaRpcCallArg("${p.parameterName}", ${val})`;
      })
      .join(',\n');
    return `using var client = new WaevaRpcClient("/api/waeva");
var result = await client.CallAsync("${ctrl}", "${m}", new[] {
${args}
});
Console.WriteLine(result.Code);`;
  });

  let copied = $state('');
  let copyTimer = $state<ReturnType<typeof setTimeout> | null>(null);

  onDestroy(() => {
    if (copyTimer) clearTimeout(copyTimer);
  });

  async function copy(text: string, label: string) {
    try {
      await navigator.clipboard.writeText(text);
      copied = label;
      if (copyTimer) clearTimeout(copyTimer);
      copyTimer = setTimeout(() => { copied = ''; copyTimer = null; }, 1500);
    } catch {
      /* ignore */
    }
  }
</script>

<div class="codegen">
  <div class="pane-header compact">
    <span class="label">Codegen</span>
    <div class="actions">
      <button class="ghost small" onclick={() => copy(tsSnippet, 'TS')}>
        {copied === 'TS' ? 'Copied!' : 'Copy TS'}
      </button>
      <button class="ghost small" onclick={() => copy(csSnippet, 'C#')}>
        {copied === 'C#' ? 'Copied!' : 'Copy C#'}
      </button>
    </div>
  </div>
  <div class="codegen-grid">
    <div class="code-block">
      <div class="code-title">TypeScript</div>
      <pre class="code"><code>{tsSnippet}</code></pre>
    </div>
    <div class="code-block">
      <div class="code-title">C#</div>
      <pre class="code"><code>{csSnippet}</code></pre>
    </div>
  </div>
</div>

<style>
  .codegen {
    flex-shrink: 0;
  }
  .compact {
    margin-top: 8px;
    padding-top: 8px;
    border-top: 1px solid var(--border-muted);
  }
  .codegen-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 8px;
  }
  .code-block {
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    padding: 8px;
    overflow: auto;
  }
  .code-title {
    font-weight: 600;
    font-size: 0.75rem;
    color: var(--text-muted);
    margin-bottom: 4px;
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }
  .code-block pre {
    margin: 0;
    white-space: pre-wrap;
    word-break: break-all;
  }
  .code-block code {
    font-family: var(--font-mono);
    font-size: 0.8rem;
    color: var(--code-text);
  }
</style>
