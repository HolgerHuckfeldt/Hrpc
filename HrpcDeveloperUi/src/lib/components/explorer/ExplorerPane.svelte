<script lang="ts">
  import { discoveryState } from '../../state/discovery.svelte.ts';
  import ControllerTree from './ControllerTree.svelte';
  import TypesTree from './TypesTree.svelte';

  let searchQuery = $state(discoveryState.searchQuery);
  $effect(() => {
    discoveryState.searchQuery = searchQuery;
  });
</script>

<div class="explorer">
  <div class="pane-header">
    <span class="label">Discovery</span>
    {#if discoveryState.loading}
      <span class="hint">Loading...</span>
    {:else if discoveryState.error}
      <span class="hint error-text">{discoveryState.error}</span>
    {:else}
      <span class="hint">
        {discoveryState.data?.controllers.length ?? 0} controllers
      </span>
    {/if}
  </div>

  <div class="search-box">
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="search-icon">
      <circle cx="11" cy="11" r="8"></circle>
      <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
    </svg>
    <input
      type="search"
      placeholder="Filter controllers or methods..."
      bind:value={searchQuery}
    />
  </div>

  <ControllerTree />

  <div class="pane-header compact">
    <span class="label">Types</span>
    <span class="hint">
      {discoveryState.data?.types ? Object.keys(discoveryState.data.types).length : 0} types
    </span>
  </div>

  <TypesTree />
</div>

<style>
  .explorer {
    display: flex;
    flex-direction: column;
    height: 100%;
    min-height: 0;
    overflow: hidden;
  }
  .search-box {
    position: relative;
    margin-bottom: 8px;
    flex-shrink: 0;
  }
  .search-box input {
    width: 100%;
    padding-left: 30px;
  }
  .search-icon {
    position: absolute;
    left: 8px;
    top: 50%;
    transform: translateY(-50%);
    color: var(--text-dim);
    pointer-events: none;
  }
  .compact {
    margin-top: 12px;
    padding-top: 8px;
    border-top: 1px solid var(--border-muted);
  }
  .error-text {
    color: var(--error);
  }
</style>
