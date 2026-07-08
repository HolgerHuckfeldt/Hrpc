import { fetchDiscovery as apiFetchDiscovery } from '../api/client';
import type { DiscoveryInfo, ControllerMeta } from '../types/discovery';

class DiscoveryState {
  data = $state<DiscoveryInfo | null>(null);
  loading = $state(false);
  error = $state<string | null>(null);
  searchQuery = $state('');

  get filteredControllers(): ControllerMeta[] {
    if (!this.data) return [];
    const q = this.searchQuery.toLowerCase().trim();
    if (!q) return this.data.controllers;

    return this.data.controllers
      .map((c) => ({
        ...c,
        methods: c.methods.filter(
          (m) =>
            c.name.toLowerCase().includes(q) ||
            m.methodName.toLowerCase().includes(q)
        ),
      }))
      .filter((c) => c.methods.length > 0 || c.name.toLowerCase().includes(q));
  }

  async fetchDiscovery() {
    this.loading = true;
    this.error = null;
    try {
      this.data = await apiFetchDiscovery();
    } catch (err) {
      this.error = err instanceof Error ? err.message : 'Discovery failed';
    } finally {
      this.loading = false;
    }
  }
}

export const discoveryState = new DiscoveryState();
