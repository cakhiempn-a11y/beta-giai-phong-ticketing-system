import { Browser, BrowserContext, Page, expect } from '@playwright/test';
import * as crypto from 'crypto';
import { E2eState, runRunnerCommand } from '../e2e-env';

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
  expect(page.url()).not.toContain('/account/login');
}

export function formatVnDateTimeLocal(utcDate: Date): string {
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

/**
 * Unseals a packed AES-256-GCM QR token cipher (12-byte nonce + 16-byte tag + ciphertext)
 * in Node.js memory without ever logging or printing the plaintext token.
 */
export function unsealQrCipherInMemory(cipherB64: string, qrAesKeyB64: string): string {
  const packed = Buffer.from(cipherB64, 'base64');
  if (packed.length < 29) {
    throw new Error('Invalid sealed QR payload length.');
  }
  const key = Buffer.from(qrAesKeyB64, 'base64');
  const nonce = packed.subarray(0, 12);
  const tag = packed.subarray(12, 28);
  const ciphertext = packed.subarray(28);

  const decipher = crypto.createDecipheriv('aes-256-gcm', key, nonce);
  decipher.setAuthTag(tag);
  const plain = Buffer.concat([decipher.update(ciphertext), decipher.final()]);
  return plain.toString('utf-8');
}

/**
 * Fetches encrypted qr_token_cipher blobs for an order and decrypts them strictly in memory.
 * Verifies each real token matches QrTokenProtector.Issue() Base64URL format: /^[A-Za-z0-9_-]{43}$/
 */
export function getOrderSensitiveQrArtifactsInMemory(
  orderId: number,
  state: E2eState
): { rawTokens: string[]; cipherB64List: string[] } {
  const res = runRunnerCommand(['e2e-sql', 'get-order-ciphers'], state, {
    BGP_E2E_ORDER_ID: String(orderId),
  });
  expect(res.status, `Failed to read ticket ciphers for order ${orderId}`).toBe(0);

  const cipherB64List = res.stdout
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.startsWith('CIPHER_B64:'))
    .map((line) => line.substring('CIPHER_B64:'.length));

  expect(cipherB64List.length).toBeGreaterThan(0);

  const rawTokens = cipherB64List.map((cipherB64) => {
    const token = unsealQrCipherInMemory(cipherB64, state.qrAesKeyB64);
    expect(
      token,
      'Issued QR token must be a 43-char Base64URL string (32 random bytes without padding)'
    ).toMatch(/^[A-Za-z0-9_-]{43}$/);
    return token;
  });

  return { rawTokens, cipherB64List };
}

export function assertNoSecretLeakInText(
  text: string,
  state: E2eState,
  label: string,
  sensitiveQrArtifacts?: { rawTokens: string[]; cipherB64List?: string[] }
): void {
  expect(text.includes(state.qrAesKeyB64), `Leak of BGP_QR_AES_KEY_B64 in ${label}`).toBe(false);
  expect(text.includes(state.mockHmacKeyB64), `Leak of BGP_MOCK_HMAC_KEY_B64 in ${label}`).toBe(false);
  expect(text.includes(state.demoPassword), `Leak of BGP_DEMO_PASSWORD in ${label}`).toBe(false);

  if (sensitiveQrArtifacts) {
    for (let i = 0; i < sensitiveQrArtifacts.rawTokens.length; i++) {
      const rawToken = sensitiveQrArtifacts.rawTokens[i];
      // Use boolean assertion so Playwright never prints the raw token value in assertion failure messages
      expect(
        text.includes(rawToken),
        `Leak of real 43-char raw QR token [#${i + 1}] in ${label}`
      ).toBe(false);
    }
    if (sensitiveQrArtifacts.cipherB64List) {
      for (let i = 0; i < sensitiveQrArtifacts.cipherB64List.length; i++) {
        const cipherB64 = sensitiveQrArtifacts.cipherB64List[i];
        expect(
          text.includes(cipherB64),
          `Leak of qr_token_cipher [#${i + 1}] in ${label}`
        ).toBe(false);
      }
    }
  }
}
