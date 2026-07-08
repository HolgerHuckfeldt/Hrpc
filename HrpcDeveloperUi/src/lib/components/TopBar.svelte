<script lang="ts">
  import { discoveryState } from '../state/discovery.svelte.ts';
  import { tabState } from '../state/tabs.svelte.ts';
  import { themeState } from '../state/theme.svelte.ts';
  import { historyState } from '../state/history.svelte.ts';

</script>

<header class="topbar">
  <div class="brand">
    <div class="dots">
      <span class="dot green"></span>
      <span class="dot blue"></span>
      <span class="dot purple"></span>
    </div>
    <span class="title">Hrpc Developer</span>
  </div>

  <div class="actions">
    <span class="pill">/api/hrpc</span>

    <button class="ghost small" onclick={() => tabState.createCodegenTab()} title="Generate client code">
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <polyline points="16 18 22 12 16 6"></polyline>
        <polyline points="8 6 2 12 8 18"></polyline>
      </svg>
      Codegen
    </button>

    <button class="ghost small" onclick={() => historyState.toggle()} title="History">
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <circle cx="12" cy="12" r="10"></circle>
        <polyline points="12 6 12 12 16 14"></polyline>
      </svg>
      History
    </button>

    <button class="ghost small" onclick={() => discoveryState.fetchDiscovery()} disabled={discoveryState.loading} title="Refresh Discovery">
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <polyline points="23 4 23 10 17 10"></polyline>
        <polyline points="1 20 1 14 7 14"></polyline>
        <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
      </svg>
      {discoveryState.loading ? 'Loading...' : 'Refresh'}
    </button>

    <button class="ghost small icon" onclick={() => themeState.toggle()} title="Toggle theme">
      {#if themeState.theme === 'dark'}
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <circle cx="12" cy="12" r="5"></circle>
          <line x1="12" y1="1" x2="12" y2="3"></line>
          <line x1="12" y1="21" x2="12" y2="23"></line>
          <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"></line>
          <line x1="18.36" y1="18.36" x2="19.78" y2="19.78"></line>
          <line x1="1" y1="12" x2="3" y2="12"></line>
          <line x1="21" y1="12" x2="23" y2="12"></line>
          <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"></line>
          <line x1="18.36" y1="5.64" x2="19.78" y2="4.22"></line>
        </svg>
      {:else}
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path>
        </svg>
      {/if}
    </button>

    <a class="ghost small" href="/swagger/index.html" target="_blank">Swagger</a>
  </div>
</header>

<style>
  .topbar {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 8px 16px;
    background: var(--bg-elevated);
    border-bottom: 1px solid var(--border);
    flex-shrink: 0;
    z-index: 100;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .dots {
    display: flex;
    gap: 5px;
  }
  .dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
  }
  .dot.green { background: var(--success); }
  .dot.blue { background: var(--accent-secondary); }
  .dot.purple { background: #8b5cf6; }
  .title {
    font-weight: 700;
    font-size: 1rem;
    letter-spacing: -0.3px;
  }
  .actions {
    display: flex;
    gap: 6px;
    align-items: center;
  }
</style>
