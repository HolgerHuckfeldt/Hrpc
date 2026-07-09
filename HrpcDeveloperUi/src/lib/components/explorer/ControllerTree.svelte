<script lang="ts">
  import { discoveryState } from '../../state/discovery.svelte.ts';
  import { tabState } from '../../state/tabs.svelte.ts';
  import type { ControllerMeta, MethodMeta } from 'hrpc-client';

  function isMethodActive(controller: ControllerMeta, method: MethodMeta): boolean {
    return tabState.activeTab?.controller?.name === controller.name && tabState.activeTab?.method?.methodName === method.methodName;
  }
</script>

<div class="tree-container">
  {#if discoveryState.filteredControllers.length === 0 && discoveryState.searchQuery}
    <div class="empty">No results for "{discoveryState.searchQuery}"</div>
  {:else}
    {#each discoveryState.filteredControllers as controller (controller.name)}
      <div class="tree-group">
        <div class="node controller">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
          </svg>
          <span>{controller.name}</span>
          <span class="badge">{controller.methods.length}</span>
        </div>
        {#each controller.methods as method (method.methodName)}
          <button
            class="node method"
            class:active={isMethodActive(controller, method)}
            onclick={() => tabState.openMethodTab(controller, method)}
          >
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>
            </svg>
            <span class="method-name">{method.methodName}</span>
            <span class="params-preview">
              ({method.parameters.map(p => p.parameterName).join(', ')})
            </span>
          </button>
        {/each}
      </div>
    {/each}
  {/if}
</div>

<style>
  .tree-container {
    flex: 1;
    overflow-y: auto;
    min-height: 0;
  }
  .tree-group {
    margin-bottom: 2px;
  }
  .node {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 5px 8px;
    border-radius: var(--radius-sm);
    cursor: pointer;
    font-size: 0.85rem;
    width: 100%;
    border: none;
    background: transparent;
    color: var(--text);
    text-align: left;
    transition: background 0.1s ease;
  }
  .node:hover {
    background: var(--bg-overlay);
  }
  .node.controller {
    font-weight: 600;
    color: var(--text);
  }
  .node.method {
    padding-left: 24px;
    color: var(--text-muted);
    font-size: 0.82rem;
  }
  .node.method.active {
    color: var(--accent-secondary);
    background: rgba(88, 166, 255, 0.08);
    font-weight: 500;
  }
  .badge {
    margin-left: auto;
    font-size: 0.7rem;
    padding: 1px 6px;
    border-radius: 999px;
    background: var(--bg-overlay);
    color: var(--text-dim);
  }
  .method-name {
    font-weight: 500;
    color: inherit;
  }
  .params-preview {
    font-size: 0.75rem;
    color: var(--text-dim);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .empty {
    padding: 16px;
    text-align: center;
    color: var(--text-muted);
    font-size: 0.85rem;
  }
</style>
