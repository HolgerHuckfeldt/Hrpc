import { defineConfig } from "vite";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));

// The generated client imports `from "trame-client"`; alias it to the runtime
// source so Vite compiles the TS directly (no stale dist). The runtime re-exports
// the websocket client, which imports the optional `ws` package — mark `ws` as
// external so the browser build never tries to resolve it (tree-shaking drops it,
// the generated client only uses the REST client).
export default defineConfig({
  resolve: {
    alias: {
      "trame-client": resolve(here, "../../../clients/ts/src/index.ts"),
    },
  },
  optimizeDeps: { exclude: ["ws"] },
  build: {
    target: "es2022",
    rollupOptions: { external: ["ws"] },
  },
  server: {
    proxy: {
      "/api": {
        target: "http://localhost:5001",
        changeOrigin: true,
      },
    },
  },
});