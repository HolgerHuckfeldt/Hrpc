import { defineConfig } from "vitest/config";

// E2E-Tests gegen eine laufende WaevaRpc-Server-Instanz.
// Opt-in: WAEVARPC_E2E=1 setzen, sonst werden die Tests übersprungen.
//   PowerShell:  $env:WAEVARPC_E2E=1; npm run test:e2e
//   Bash:        WAEVARPC_E2E=1 npm run test:e2e
// Server z.B.:   dotnet run --project WaevaRpc
// Ziel-URL:      WAEVARPC_URL (Default http://127.0.0.1:5001)
export default defineConfig({
  test: {
    include: ["test/e2e/**/*.test.ts"],
    environment: "node",
    globals: false,
    testTimeout: 20000,
  },
});