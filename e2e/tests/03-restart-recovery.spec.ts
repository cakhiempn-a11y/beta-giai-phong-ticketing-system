import { test, expect } from '@playwright/test';
import * as crypto from 'crypto';
import * as fs from 'fs';
import * as path from 'path';
import {
  createInitialE2eState,
  loadE2eState,
  runGlobalSetupLifecycle,
  runGlobalTeardownLifecycle,
  runRunnerCommand,
  saveE2eState,
  spawnWebServer,
} from '../e2e-env';
import {
  assertNoSecretLeakInText,
  createRoleSession,
  formatVnDateTimeLocal,
  getOrderSensitiveQrArtifactsInMemory,
  loginInBrowser,
} from './helpers';

function signMockWebhook(
  hmacKeyB64: string,
  payload: {
    providerEventRef: string;
    attemptKey: string;
    providerTransactionRef: string;
    orderCode: string;
    amountVnd: number;
    outcome: string;
  }
): string {
  const canonical = [
    'MOCK',
    payload.providerEventRef,
    payload.attemptKey,
    payload.providerTransactionRef,
    payload.orderCode,
    String(payload.amountVnd),
    payload.outcome,
  ].join('\n');
  const keyBytes = Buffer.from(hmacKeyB64, 'base64');
  return crypto.createHmac('sha256', keyBytes).update(canonical, 'utf8').digest('hex').toUpperCase();
}

test.describe('BE-05B Phase 7: Restart, Recovery, Idempotency & Failure Behavior', () => {
  test('7.1-7.5: Web restart preserves PAID order & QR, duplicate webhook idempotency, network failure UI, expiry worker, and Runner verify', async ({ browser }) => {
    const state = loadE2eState();
    const secondaryPort = 5189;

    // 7.1 Spawn an isolated Web server instance on port 5189 sharing the E2E database and same AES/HMAC keys
    let server1 = await spawnWebServer(state, secondaryPort);
    const recoveryBaseUrl = server1.baseUrl;

    const managerSession = await createRoleSession(browser, recoveryBaseUrl);
    const customerSession = await createRoleSession(browser, recoveryBaseUrl);
    const staffSession = await createRoleSession(browser, recoveryBaseUrl);

    try {
      const managerPage = managerSession.page;
      const customerPage = customerSession.page;
      const staffPage = staffSession.page;

      managerPage.on('dialog', (d) => d.accept());

      await loginInBrowser(managerPage, 'manager@demo.invalid', state.demoPassword);
      await loginInBrowser(customerPage, 'customer1@demo.invalid', state.demoPassword);
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);

      // Manager creates a movie & showtime at now + 18m on a non-conflicting schedule
      await managerPage.goto('/management/movies/create');
      const suffix = Date.now().toString().slice(-6);
      const movieTitle = `Phim Recovery E2E ${suffix}`;
      await managerPage.fill('input[name="Title"]', movieTitle);
      await managerPage.fill('input[name="DurationMin"]', '95');
      await managerPage.selectOption('select[name="AgeLabel"]', 'T16');
      await managerPage.fill('input[name="GenreText"]', 'Hành động');
      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click('main button[type="submit"]'),
      ]);
      const movieId = Number(managerPage.url().match(/\/management\/movies\/(\d+)$/)![1]);

      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click(`form[action*="/management/movies/${movieId}/publish"] button[type="submit"]`),
      ]);

      // Use the existing demo showtime or publish a showtime at tomorrow + 6 hours for webhook/expiry tests,
      // OR publish a showtime at now + 15m if room is free. Let's check existing showtimes first:
      // Actually in 01 and 02 we already created showtimes at now + 15m and now + 20m in Room 1!
      // Let's find showtimeA from 01-full-journey or 02-negative on /movies!
      await customerPage.goto('/movies');
      // Let's find the first movie that has an upcoming showtime and open its seat map
      const firstMovieDetailLink = customerPage.locator('a[href*="/movies/"]').first();
      await firstMovieDetailLink.click();
      const firstShowtimeSeatLink = customerPage.locator('a[href*="/showtimes/"][href*="/seats"]').first();
      const showtimeHref = (await firstShowtimeSeatLink.getAttribute('href'))!;
      const showtimeId = Number(showtimeHref.match(/\/showtimes\/(\d+)\/seats/)![1]);

      // Customer reserves 1 seat and pays via Signed Webhook (7.2 Duplicate Webhook Idempotency)
      await customerPage.goto(`/showtimes/${showtimeId}/seats`);
      await customerPage.locator('.seat-box[data-bookable="true"]').first().click();
      await Promise.all([
        customerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        customerPage.click('#btnSubmit'),
      ]);
      const paidOrderId = Number(customerPage.url().match(/\/orders\/(\d+)$/)![1]);

      // Fetch order details via API to get orderCode and totalVnd
      const orderDetailResp = await customerSession.context.request.get(`/api/orders/${paidOrderId}`);
      expect(orderDetailResp.status()).toBe(200);
      const orderDetail = await orderDetailResp.json();
      const orderCode: string = orderDetail.orderCode;
      const totalVnd: number = orderDetail.totalVnd;

      // Get XSRF-TOKEN cookie for customer API POST
      const cookies = await customerSession.context.cookies();
      const xsrfCookie = cookies.find((c) => c.name === 'XSRF-TOKEN');
      expect(xsrfCookie).toBeDefined();

      // Create mock payment attempt via API
      const attemptKey = `REC-ATTEMPT-${Date.now()}`;
      const attemptResp = await customerSession.context.request.post(
        `/api/orders/${paidOrderId}/payments/mock-attempt`,
        {
          headers: { 'X-XSRF-TOKEN': xsrfCookie!.value },
          data: { attemptKey },
        }
      );
      expect(attemptResp.status()).toBe(201);

      // 7.2 Send signed webhook #1 (APPLIED) and duplicate signed webhook #2 (DUPLICATE)
      const webhookPayload = {
        providerEventRef: `EVT-REC-${Date.now()}`,
        attemptKey,
        providerTransactionRef: `TXN-REC-${Date.now()}`,
        orderCode,
        amountVnd: totalVnd,
        outcome: 'SUCCESS',
      };
      const signature = signMockWebhook(state.mockHmacKeyB64, webhookPayload);

      const whResp1 = await customerSession.context.request.post('/api/webhooks/mock-payment', {
        headers: { 'X-Signature-SHA256': signature },
        data: webhookPayload,
      });
      expect(whResp1.status()).toBe(200);
      const whBody1 = await whResp1.json();
      expect(whBody1.decision).toBe('APPLIED');
      expect(whBody1.orderStatus).toBe('PAID');

      // Duplicate webhook with same providerEventRef & attemptKey -> DUPLICATE (idempotent, no extra tickets)
      const whResp2 = await customerSession.context.request.post('/api/webhooks/mock-payment', {
        headers: { 'X-Signature-SHA256': signature },
        data: webhookPayload,
      });
      expect(whResp2.status()).toBe(200);
      const whBody2 = await whResp2.json();
      expect(whBody2.decision).toBe('DUPLICATE');
      expect(whBody2.orderStatus).toBe('PAID');

      // Verify exactly 1 ticket was issued
      await customerPage.goto(`/orders/${paidOrderId}/tickets`);
      await expect(customerPage.locator('.ticket-card')).toHaveCount(1);

      // 7.4 Also create a second reservation (PENDING_PAYMENT) and set its expiry in the past to test ReservationExpiryWorker
      await customerPage.goto(`/showtimes/${showtimeId}/seats`);
      await customerPage.locator('.seat-box[data-bookable="true"]').first().click();
      await Promise.all([
        customerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        customerPage.click('#btnSubmit'),
      ]);
      const pendingOrderId = Number(customerPage.url().match(/\/orders\/(\d+)$/)![1]);

      // Set expires_utc in the past for pendingOrderId
      runRunnerCommand(['e2e-sql', 'force-expire-order'], state, {
        BGP_E2E_ORDER_ID: String(pendingOrderId),
      });

      // Wait ~3 seconds for ReservationExpiryWorker (interval = 2s) to sweep and expire pendingOrderId
      await new Promise((r) => setTimeout(r, 3500));

      await customerPage.goto(`/orders/${pendingOrderId}`);
      await expect(customerPage.locator('body')).toContainText(/EXPIRED|Hết hạn/);

      // 7.1 Stop Web Server #1 and restart a fresh Web Server #2 on the same port with same DB & keys
      await server1.stop();
      server1 = await spawnWebServer(state, secondaryPort);

      // Clear old cookies so we authenticate cleanly against the newly started Web Server #2
      await customerSession.context.clearCookies();
      await staffSession.context.clearCookies();

      await loginInBrowser(customerPage, 'customer1@demo.invalid', state.demoPassword);
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);

      // Verify PAID order still exists, is PAID, and QR PNG still loads & decodes after server restart!
      await customerPage.goto(`/orders/${paidOrderId}/tickets`);
      await expect(customerPage.locator('body')).toContainText('PAID');
      await expect(customerPage.locator('.ticket-card')).toHaveCount(1);

      const qrImg = customerPage.locator('img[src*="/tickets/"][src*="/qr"]').first();
      await expect
        .poll(
          async () =>
            await qrImg.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0),
          { timeout: 10_000 }
        )
        .toBe(true);

      const qrSrc = (await qrImg.getAttribute('src'))!;
      const qrPngResp = await customerSession.context.request.get(qrSrc);
      expect(qrPngResp.status()).toBe(200);

      const tmpDir = path.join(__dirname, '..', '.tmp');
      if (!fs.existsSync(tmpDir)) fs.mkdirSync(tmpDir, { recursive: true });
      const qrFilePath = path.join(tmpDir, `rec-qr-${paidOrderId}.png`);
      fs.writeFileSync(qrFilePath, await qrPngResp.body());

      // Reload tickets page to confirm zero duplicate tickets
      await customerPage.reload({ waitUntil: 'domcontentloaded' });
      await expect(customerPage.locator('.ticket-card')).toHaveCount(1);

      // 7.3 Simulate transient network / API failure on /api/staff/tickets/scan first, then allow retry to succeed!
      let interceptCount = 0;
      await staffPage.route('**/api/staff/tickets/scan', async (route) => {
        interceptCount++;
        if (interceptCount === 1) {
          await route.abort('failed');
        } else {
          await route.continue();
        }
      });

      await staffPage.goto(`/staff/check-in?showtimeId=${showtimeId}`);
      staffPage.on('dialog', (d) => d.accept());
      await expect(staffPage.locator('#showtimeSelect')).toHaveValue(String(showtimeId));

      // Upload QR PNG -> first attempt fails with network abort -> UI displays "LỖI KẾT NỐI"
      await staffPage.locator('#qrFileInput').setInputFiles(qrFilePath);
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('LỖI KẾT NỐI', { timeout: 15_000 });
      const retryBtn = staffPage.locator('button:has-text("Thử lại kết nối")');
      await expect(retryBtn).toBeVisible();

      // Click retry ("Thử lại kết nối") -> route continues -> scan succeeds with "HỢP LỆ" (ACCEPTED)!
      await retryBtn.click();
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('HỢP LỆ', { timeout: 15_000 });
      await expect(staffPage.locator('#resultCard')).toContainText('Soát vé thành công');

      // 7.5 Verify all 9 database invariants pass via Runner verify on main E2E database
      const verifyRes = runRunnerCommand(['verify'], state);
      expect(verifyRes.status).toBe(0);
      expect(verifyRes.stdout).toContain('INTEGRITY PASSED');

      const sensitiveQr = getOrderSensitiveQrArtifactsInMemory(paidOrderId, state);
      if (fs.existsSync(qrFilePath)) {
        fs.unlinkSync(qrFilePath);
      }
      for (const s of [managerSession, customerSession, staffSession]) {
        for (const m of s.consoleMessages) {
          assertNoSecretLeakInText(m, state, 'Browser Console', sensitiveQr);
        }
      }

      // 7.6 (BE-05C Phase 0.1): Verify globalTeardown FAILS quality gate when Runner verify detects a violation,
      // while STILL executing finally cleanup (dropping database and removing state file).
      const gateState = createInitialE2eState(`gate_${crypto.randomBytes(2).toString('hex')}`, 5192);
      const gateStateFile = path.join(tmpDir, `gate-state-${Date.now()}.json`);
      const createGateRes = runRunnerCommand(
        ['create-e2e-db', gateState.dbName, gateState.ownershipToken],
        gateState
      );
      expect(createGateRes.status).toBe(0);
      saveE2eState(gateState, gateStateFile);

      const corruptRes = runRunnerCommand(['e2e-sql', 'corrupt-e2e-db'], gateState);
      expect(corruptRes.status).toBe(0);

      let gateError: Error | null = null;
      try {
        await runGlobalTeardownLifecycle(gateStateFile, { forceAutoCleanup: true });
      } catch (err: any) {
        gateError = err;
      }
      expect(gateError).not.toBeNull();
      expect(gateError!.message).toContain('Runner verify failed');

      // Confirm finally block still dropped the corrupted DB and removed the state file
      const gateExistsRes = runRunnerCommand(['e2e-sql', 'check-db-exists'], gateState);
      expect(gateExistsRes.stdout).toContain('DB_EXISTS:0');
      expect(fs.existsSync(gateStateFile)).toBe(false);

      // 7.7 (BE-05C Phase 0.2): Verify globalSetup cleans up newly created DB and state file if health check fails
      const setupFailState = createInitialE2eState(`sfail_${crypto.randomBytes(2).toString('hex')}`, 5193);
      const setupFailStateFile = path.join(tmpDir, `setupfail-state-${Date.now()}.json`);
      let setupFailError: Error | null = null;
      try {
        await runGlobalSetupLifecycle(setupFailState, {
          stateFilePath: setupFailStateFile,
          healthTimeoutMs: 1600,
          healthPath: '/nonexistent-health-endpoint-should-fail-404',
        });
      } catch (err: any) {
        setupFailError = err;
      }
      expect(setupFailError).not.toBeNull();
      expect(setupFailError!.message).toContain('Failed to start Web server');

      const setupFailExistsRes = runRunnerCommand(['e2e-sql', 'check-db-exists'], setupFailState);
      expect(setupFailExistsRes.stdout).toContain('DB_EXISTS:0');
      expect(fs.existsSync(setupFailStateFile)).toBe(false);
    } finally {
      await managerSession.context.close();
      await customerSession.context.close();
      await staffSession.context.close();
      await server1.stop();
    }
  });
});
