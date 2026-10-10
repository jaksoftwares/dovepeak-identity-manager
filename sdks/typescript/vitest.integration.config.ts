import { defineConfig } from "vitest/config";

// Contract tests against the running local stack (docker compose up).
export default defineConfig({
  test: { include: ["test-integration/**/*.test.ts"], environment: "node", testTimeout: 60_000, hookTimeout: 60_000 },
});
