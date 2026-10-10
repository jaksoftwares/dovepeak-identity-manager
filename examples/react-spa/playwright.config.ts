import { defineConfig, devices } from "@playwright/test";

// End-to-end test of the SPA against the local stack. Start the app first (npm run preview) and the protected API
// with CORS for http://localhost:5173 (see the README).
export default defineConfig({
  testDir: "./e2e",
  workers: 1,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  reporter: [["list"]],
  use: { baseURL: process.env.SPA_URL ?? "http://localhost:5173", trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
