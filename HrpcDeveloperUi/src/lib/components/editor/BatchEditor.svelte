<script lang="ts">
  import { ExecutionMode, type HrpcRequest } from 'hrpc-client';
  import { formatJson } from '../../utils/json';

  let {
    requests = [],
    mode = ExecutionMode.Parallel,
    onchange,
  }: {
    requests: HrpcRequest[];
    mode: ExecutionMode;
    onchange?: (requests: HrpcRequest[], mode: ExecutionMode) => void;
  } = $props();

  let idCounter = $state(requests.length);

  function addRequest() {
    idCounter++;
    requests.push({
      controller: '',
      method: '',
      stringData: '[]',
      id: `req-${idCounter}`,
    });
    requests = [...requests];
    onchange?.(requests, mode);
  }

  function removeRequest(index: number) {
    requests.splice(index, 1);
    requests = [...requests];
    onchange?.(requests, mode);
  }

  // Only allow updating string fields (controller, method, id) via text inputs
  type StringField = 'controller' | 'method' | 'id';
  const stringFields: StringField[] = ['controller', 'method', 'id'];

  function updateRequest(index: number, field: StringField, value: string) {
    requests[index] = { ...requests[index], [field]: value };
    requests = [...requests];
    onchange?.(requests, mode);
  }

  function updateMode(newMode: ExecutionMode) {
    mode = newMode;
    onchange?.(requests, mode);
  }
</script>

<div class="batch-editor">
  <div class="batch-header">
    <span class="field-label">Batch Request ({requests.length} calls)</span>
    <div class="mode-switch">
      <button
        class="ghost small"
        class:active={mode === ExecutionMode.Parallel}
        onclick={() => updateMode(ExecutionMode.Parallel)}
      >Parallel</button>
      <button
        class="ghost small"
        class:active={mode === ExecutionMode.Serial}
        onclick={() => updateMode(ExecutionMode.Serial)}
      >Serial</button>
    </div>
    <button class="ghost small" onclick={addRequest}>+ Add</button>
  </div>

  <div class="batch-list">
    {#each requests as req, i (i)}
      <div class="batch-row">
        <span class="row-index">{i + 1}</span>
        <input
          class="ctrl-input"
          placeholder="Controller"
          value={req.controller}
          oninput={(e) => updateRequest(i, 'controller', (e.target as HTMLInputElement).value)}
        />
        <input
          class="method-input"
          placeholder="Method"
          value={req.method}
          oninput={(e) => updateRequest(i, 'method', (e.target as HTMLInputElement).value)}
        />
        <input
          class="id-input"
          placeholder="ID"
          value={req.id ?? ''}
          oninput={(e) => updateRequest(i, 'id', (e.target as HTMLInputElement).value)}
        />
        <button class="ghost small icon" onclick={() => removeRequest(i)} title="Remove">
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>
      </div>
    {/each}
  </div>
</div>

<style>
  .batch-editor {
    flex-shrink: 0;
  }
  .batch-header {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 6px;
  }
  .batch-header .field-label {
    margin: 0;
    flex: 1;
  }
  .mode-switch {
    display: flex;
    gap: 2px;
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    overflow: hidden;
  }
  .mode-switch button {
    border: none;
    border-radius: 0;
    padding: 3px 10px;
    font-size: 0.8rem;
  }
  .mode-switch button.active {
    background: var(--accent-secondary);
    color: #fff;
  }
  .batch-list {
    display: flex;
    flex-direction: column;
    gap: 4px;
    max-height: 200px;
    overflow-y: auto;
  }
  .batch-row {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .row-index {
    width: 20px;
    text-align: center;
    font-size: 0.75rem;
    color: var(--text-dim);
    flex-shrink: 0;
  }
  .ctrl-input {
    flex: 2;
    min-width: 0;
  }
  .method-input {
    flex: 2;
    min-width: 0;
  }
  .id-input {
    flex: 1;
    min-width: 0;
  }
  .batch-row input {
    font-size: 0.8rem;
    padding: 4px 6px;
  }
</style>
