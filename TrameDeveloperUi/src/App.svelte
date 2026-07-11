<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { discoveryState } from './lib/state/discovery.svelte.ts';
  import { tabState } from './lib/state/tabs.svelte.ts';
  import TopBar from './lib/components/TopBar.svelte';
  import ExplorerPane from './lib/components/explorer/ExplorerPane.svelte';
  import EditorPane from './lib/components/editor/EditorPane.svelte';
  import ResultPane from './lib/components/result/ResultPane.svelte';
  import HistoryPanel from './lib/components/history/HistoryPanel.svelte';

  let leftPane = $state<HTMLDivElement | null>(null);
  let centerPane = $state<HTMLDivElement | null>(null);
  let rightPane = $state<HTMLDivElement | null>(null);
  let leftWidth = $state(280);
  let rightWidth = $state(360);
  let dragging = $state<'left' | 'right' | null>(null);
  let dragCleanup = $state<(() => void) | null>(null);

  onDestroy(() => {
    dragCleanup?.();
  });

  const MIN_LEFT = 200;
  const MIN_CENTER = 320;
  const MIN_RIGHT = 260;

  function loadSizes() {
    try {
      const saved = localStorage.getItem('trame-split-sizes');
      if (saved) {
        const [l, r] = JSON.parse(saved);
        if (typeof l === 'number' && l >= MIN_LEFT) leftWidth = l;
        if (typeof r === 'number' && r >= MIN_RIGHT) rightWidth = r;
      }
    } catch { /* ignore */ }
  }

  function saveSizes() {
    try {
      localStorage.setItem('trame-split-sizes', JSON.stringify([leftWidth, rightWidth]));
    } catch { /* ignore */ }
  }

  function onSplitterDown(which: 'left' | 'right', e: MouseEvent) {
    e.preventDefault();
    dragging = which;
    const startX = e.clientX;
    const startLeft = leftWidth;
    const startRight = rightWidth;

    function onMove(ev: MouseEvent) {
      const dx = ev.clientX - startX;
      if (which === 'left') {
        leftWidth = Math.max(MIN_LEFT, Math.min(startLeft + dx, startLeft + startRight + (centerPane?.getBoundingClientRect().width ?? 400) - MIN_CENTER - MIN_RIGHT));
      } else {
        rightWidth = Math.max(MIN_RIGHT, Math.min(startRight - dx, startRight + startLeft + (centerPane?.getBoundingClientRect().width ?? 400) - MIN_LEFT - MIN_CENTER));
      }
    }

    function onUp() {
      dragging = null;
      dragCleanup = null;
      saveSizes();
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
    }

    dragCleanup = () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
    };

    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
  }

  onMount(() => {
    loadSizes();
    // Welcome-Tab nur beim allerersten Start (kein persistierter Tab-Zustand);
    // sonst werden persistierte Tabs aus localStorage wiederhergestellt.
    if (tabState.tabs.length === 0) {
      tabState.createTab({ title: 'Welcome', requestText: '[]', params: [] });
    }
    discoveryState.fetchDiscovery();
  });
</script>

<div class="app-shell" class:is-dragging={dragging !== null}>
  <TopBar />

  <div class="main-content">
    <div class="pane pane-left" style="width: {leftWidth}px" bind:this={leftPane}>
      <ExplorerPane />
    </div>

    <div
      class="splitter"
      class:active={dragging === 'left'}
      onmousedown={(e) => onSplitterDown('left', e)}
      role="separator"
      tabindex="-1"
    ></div>

    <div class="pane pane-center" bind:this={centerPane}>
      <EditorPane />
    </div>

    <div
      class="splitter"
      class:active={dragging === 'right'}
      onmousedown={(e) => onSplitterDown('right', e)}
      role="separator"
      tabindex="-1"
    ></div>

    <div class="pane pane-right" style="width: {rightWidth}px" bind:this={rightPane}>
      <ResultPane />
    </div>
  </div>

  <HistoryPanel />

  <footer class="footer">
    <span>Trame · JSON RPC with batching, dependency mapping, MessagePack & SignalR</span>
    <div class="footer-links">
      <a href="https://github.com" target="_blank">GitHub</a>
      <a href="/swagger/index.html" target="_blank">Swagger</a>
    </div>
  </footer>
</div>

<style>
  .app-shell {
    display: flex;
    flex-direction: column;
    height: 100vh;
    overflow: hidden;
  }

  .main-content {
    display: flex;
    flex: 1;
    min-height: 0;
    padding: 8px;
    gap: 0;
    overflow: hidden;
  }

  .pane {
    display: flex;
    flex-direction: column;
    min-height: 0;
    overflow: hidden;
    background: var(--bg-elevated);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 10px;
    flex-shrink: 0;
  }

  .pane-left {
    width: 280px;
  }

  .pane-center {
    flex: 1;
    min-width: 320px;
    margin: 0 4px;
  }

  .pane-right {
    width: 360px;
  }

  .is-dragging {
    cursor: col-resize;
    user-select: none;
  }
  .splitter {
    width: 6px;
    cursor: col-resize;
    background: transparent;
    flex-shrink: 0;
    transition: background 0.15s ease;
    position: relative;
    z-index: 10;
  }
  .splitter:hover,
  .splitter.active {
    background: var(--accent-secondary);
    opacity: 0.4;
  }

  .footer {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 6px 16px;
    font-size: 0.75rem;
    color: var(--text-dim);
    border-top: 1px solid var(--border);
    background: var(--bg-elevated);
    flex-shrink: 0;
  }
  .footer-links {
    display: flex;
    gap: 12px;
  }
</style>
