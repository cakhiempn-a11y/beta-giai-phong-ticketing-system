import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.BGP_E2E_BASE_URL || 'http://127.0.0.1:5188';
const allowLocalTrace = process.env.BGP_E2E_CAPTURE_TRACES === 'true';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 1,
  timeout: 120_000,
  expect: {
    timeout: 15_000,
  },
  reporter: [
    ['list'],
    ['html', { outputFolder: 'playwright-report', open: 'never' }],
  ],
  globalSetup: require.resolve('./global-setup'),
  globalTeardown: require.resolve('./global-teardown'),
  use: {
    baseURL,
    // Phase 0.4: Do not capture screenshots/videos/traces containing rendered QR codes or credentials in CI artifacts
    trace: allowLocalTrace ? 'retain-on-failure' : 'off',
    screenshot: 'off',
    video: 'off',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
