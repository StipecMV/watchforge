import { defineConfig, devices } from '@playwright/test';

/**
 * WatchForge Web UI E2E (S11, Playwright).
 *
 * Predpokladá dva webServer-y (štartujú sa automaticky):
 *  - API na http://localhost:5000 (dotnet, seeded DB — pozri scripts/e2e-api.sh)
 *  - Angular dev server na http://localhost:4200
 *
 * Spustenie:
 *   bunx playwright test
 *   bunx playwright test --headed
 *   bunx playwright test --ui
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1, // API zdieľa jednu seeded DB — testy idú sekvenčne
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list']],
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'bash scripts/e2e-api.sh',
      url: 'http://localhost:5000/api/v1/system/health',
      reuseExistingServer: true,
      timeout: 90_000,
    },
    {
      command: 'bunx ng serve --port 4200 --live-reload false',
      url: 'http://localhost:4200',
      reuseExistingServer: true,
      timeout: 180_000, // prvý Angular build môže trvať dlhšie
    },
  ],
});
