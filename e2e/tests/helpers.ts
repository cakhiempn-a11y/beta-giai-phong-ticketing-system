import { Browser, BrowserContext, Page, expect } from '@playwright/test';
import { E2eState } from '../e2e-env';

export interface RoleSession {
  context: BrowserContext;
  page: Page;
  consoleMessages: string[];
  pageErrors: string[];
}

export async function createRoleSession(
  browser: Browser,
  baseURL: string
): Promise<RoleSession> {
  const context = await browser.newContext({ baseURL });
  const page = await context.newPage();
  const consoleMessages: string[] = [];
  const pageErrors: string[] = [];

  page.on('console', (msg) => {
    consoleMessages.push(`[${msg.type()}] ${msg.text()}`);
  });
  page.on('pageerror', (err) => {
    pageErrors.push(err.message);
  });

  return { context, page, consoleMessages, pageErrors };
}

export async function loginInBrowser(
  page: Page,
  email: string,
  password: string
): Promise<void> {
  await page.goto('/account/login');
  if (!page.url().includes('/account/login')) {
    return;
  }
  await expect(page.locator('input[name="Request.Email"]')).toBeVisible();
  await page.fill('input[name="Request.Email"]', email);
  await page.fill('input[name="Request.Password"]', password);
  await Promise.all([
    page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
    page.click('main button[type="submit"]'),
  ]);
  // Verify user is logged in (no longer on /account/login with error)
  expect(page.url()).not.toContain('/account/login');
}

export function formatVnDateTimeLocal(utcDate: Date): string {
  // Convert UTC date to Vietnam time (UTC+7) formatted as YYYY-MM-DDTHH:mm
  const vnTime = new Date(utcDate.getTime() + 7 * 60 * 60 * 1000);
  const yyyy = vnTime.getUTCFullYear();
  const mm = String(vnTime.getUTCMonth() + 1).padStart(2, '0');
  const dd = String(vnTime.getUTCDate()).padStart(2, '0');
  const hh = String(vnTime.getUTCHours()).padStart(2, '0');
  const min = String(vnTime.getUTCMinutes()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}T${hh}:${min}`;
}

export function formatVnDateOnly(utcDate: Date): string {
  const vnTime = new Date(utcDate.getTime() + 7 * 60 * 60 * 1000);
  const yyyy = vnTime.getUTCFullYear();
  const mm = String(vnTime.getUTCMonth() + 1).padStart(2, '0');
  const dd = String(vnTime.getUTCDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

export function assertNoSecretLeakInText(
  text: string,
  state: E2eState,
  label: string
): void {
  expect(text, `Leak of BGP_QR_AES_KEY_B64 in ${label}`).not.toContain(state.qrAesKeyB64);
  expect(text, `Leak of BGP_MOCK_HMAC_KEY_B64 in ${label}`).not.toContain(state.mockHmacKeyB64);
  expect(text, `Leak of BGP_DEMO_PASSWORD in ${label}`).not.toContain(state.demoPassword);
  expect(text, `Leak of raw QR token prefix BGP1. in ${label}`).not.toContain('BGP1.');
}
