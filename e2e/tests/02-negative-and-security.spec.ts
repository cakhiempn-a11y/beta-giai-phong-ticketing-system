import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { loadE2eState, runRunnerCommand } from '../e2e-env';
import {
  assertNoSecretLeakInText,
  createRoleSession,
  formatVnDateTimeLocal,
  getOrderSensitiveQrArtifactsInMemory,
  loginInBrowser,
} from './helpers';

test.describe('BE-05B Phase 6: Negative, Concurrency, Authorization & Security E2E Scenarios', () => {
  test('6.1 & 6.2 & 6.3 & 6.4: Customer, Staff, Manager negative flows and security guards', async ({ browser }) => {
    const state = loadE2eState();
    const baseURL = state.baseUrl;

    const managerSession = await createRoleSession(browser, baseURL);
    const cust1Session = await createRoleSession(browser, baseURL);
    const cust2Session = await createRoleSession(browser, baseURL);
    const staffSession = await createRoleSession(browser, baseURL);
    const guestSession = await createRoleSession(browser, baseURL);

    try {
      const managerPage = managerSession.page;
      const cust1Page = cust1Session.page;
      const cust2Page = cust2Session.page;
      const staffPage = staffSession.page;
      const guestPage = guestSession.page;

      managerPage.on('dialog', (d) => d.accept());
      cust1Page.on('dialog', (d) => d.accept());
      cust2Page.on('dialog', (d) => d.accept());
      staffPage.on('dialog', (d) => d.accept());

      await loginInBrowser(managerPage, 'manager@demo.invalid', state.demoPassword);
      await loginInBrowser(cust1Page, 'customer1@demo.invalid', state.demoPassword);
      await loginInBrowser(cust2Page, 'customer2@demo.invalid', state.demoPassword);
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);

      // -----------------------------------------------------------------------
      // Setup: Find Showtime A from 01-full-journey (starts ~15m in future, inside check-in window)
      // and publish Showtime B (starts +240m in future, outside 30-min check-in window)
      // -----------------------------------------------------------------------
      await cust1Page.goto('/movies');
      const existingMovieHref = (await cust1Page.locator('a[href*="/movies/"]').first().getAttribute('href'))!;
      await cust1Page.goto(existingMovieHref);
      const existingSeatHref = (await cust1Page.locator('a[href*="/showtimes/"][href*="/seats"]').first().getAttribute('href'))!;
      const showtimeAId = Number(existingSeatHref.match(/\/showtimes\/(\d+)\/seats/)![1]);

      // Fetch showtimeA details from Manager page to know its room and start time
      await managerPage.goto(`/management/showtimes/${showtimeAId}`);
      await expect(managerPage.locator('body')).toContainText('ON_SALE');

      // Manager creates a new movie for negative lifecycle & Showtime B
      await managerPage.goto('/management/movies/create');
      const suffix = Date.now().toString().slice(-6);
      const movieTitle = `Phim Negative E2E ${suffix}`;
      await managerPage.fill('input[name="Title"]', movieTitle);
      await managerPage.fill('input[name="DurationMin"]', '90');
      await managerPage.selectOption('select[name="AgeLabel"]', 'P');
      await managerPage.fill('input[name="GenreText"]', 'Tâm lý');
      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click('main button[type="submit"]'),
      ]);
      const movieId = Number(managerPage.url().match(/\/management\/movies\/(\d+)$/)![1]);

      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click(`form[action*="/management/movies/${movieId}/publish"] button[type="submit"]`),
      ]);

      await managerPage.goto('/management/showtimes/create');
      const roomOption = managerPage.locator('select[name="RoomId"] option:not([value=""])').first();
      const roomId = Number(await roomOption.getAttribute('value'));

      // -----------------------------------------------------------------------
      // 6.3 MANAGER NEGATIVE: Room schedule overlap (same room, now + 25m overlaps Showtime A)
      // -----------------------------------------------------------------------
      await managerPage.selectOption('select[name="MovieId"]', String(movieId));
      await managerPage.selectOption('select[name="RoomId"]', String(roomId));
      const overlappingStart = new Date(Date.now() + 25 * 60 * 1000);
      await managerPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(overlappingStart));
      await managerPage.fill('input[name="BufferMinutes"]', '15');
      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click('main button[type="submit"]'),
      ]);
      // Must stay on create page with overlap validation error
      await expect(managerPage).toHaveURL(/\/management\/showtimes\/create/);
      await expect(managerPage.locator('body')).toContainText(/trùng|xung đột|lịch chiếu/i);

      // Publish Showtime B (now + 240m, non-overlapping, outside check-in window)
      await managerPage.selectOption('select[name="MovieId"]', String(movieId));
      await managerPage.selectOption('select[name="RoomId"]', String(roomId));
      const startB = new Date(Date.now() + 240 * 60 * 1000);
      await managerPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(startB));
      await managerPage.fill('input[name="BufferMinutes"]', '15');
      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click('main button[type="submit"]'),
      ]);
      const showtimeBId = Number(managerPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);

      // -----------------------------------------------------------------------
      // 6.3 MANAGER NEGATIVE: Archive movie while active future showtime exists
      // -----------------------------------------------------------------------
      await managerPage.goto(`/management/movies/${movieId}`);
      const archiveBtn = managerPage.locator(`form[action*="/management/movies/${movieId}/archive"] button[type="submit"]`);
      if ((await archiveBtn.count()) > 0 && (await archiveBtn.isEnabled())) {
        await Promise.all([
          managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          archiveBtn.click(),
        ]);
        await expect(managerPage.locator('.alert-danger')).toBeVisible();
      }
      // Movie must still be PUBLISHED
      await expect(managerPage.locator('.badge', { hasText: 'PUBLISHED' }).first()).toBeVisible();

      // -----------------------------------------------------------------------
      // 6.1 CUSTOMER NEGATIVE: Guest cannot reserve seats -> redirected to Login
      // -----------------------------------------------------------------------
      await guestPage.goto(`/showtimes/${showtimeAId}/seats`);
      await guestPage.locator('.seat-box[data-bookable="true"]').first().click();
      await Promise.all([
        guestPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        guestPage.click('#btnSubmit'),
      ]);
      await expect(guestPage).toHaveURL(/\/account\/login/);

      // -----------------------------------------------------------------------
      // 6.1 CUSTOMER NEGATIVE: Selecting > 8 seats blocked by UI & backend
      // -----------------------------------------------------------------------
      await cust1Page.goto(`/showtimes/${showtimeBId}/seats`);
      const bookableBoxes = cust1Page.locator('.seat-box[data-bookable="true"]');
      for (let i = 0; i < 9; i++) {
        await bookableBoxes.nth(i).click();
      }
      // UI caps selection at 8 seats
      await expect(cust1Page.locator('#selectedCount')).toHaveText('8');

      // -----------------------------------------------------------------------
      // 6.1 CUSTOMER NEGATIVE: Two customers concurrently reserving the same seat
      // -----------------------------------------------------------------------
      await cust1Page.goto(`/showtimes/${showtimeAId}/seats`);
      await cust2Page.goto(`/showtimes/${showtimeAId}/seats`);

      const targetRaceSeat1 = cust1Page.locator('.seat-box[data-bookable="true"]').nth(3);
      const raceSeatCode = (await targetRaceSeat1.getAttribute('data-seat-code'))!;
      const targetRaceSeat2 = cust2Page.locator(`.seat-box[data-seat-code="${raceSeatCode}"]`);

      await targetRaceSeat1.click();
      await targetRaceSeat2.click();

      // Submit both forms concurrently
      await Promise.all([
        Promise.all([
          cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          cust1Page.click('#btnSubmit'),
        ]),
        Promise.all([
          cust2Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          cust2Page.click('#btnSubmit'),
        ]),
      ]);

      const url1 = cust1Page.url();
      const url2 = cust2Page.url();
      const cust1Won = /\/orders\/\d+$/.test(url1);
      const cust2Won = /\/orders\/\d+$/.test(url2);

      // Exactly one succeeds and is redirected to /orders/{id}; the other is redirected back to /showtimes/{id}/seats with error
      expect(cust1Won !== cust2Won, `Expected exactly 1 winner: url1=${url1}, url2=${url2}`).toBe(true);

      const loserPage = cust1Won ? cust2Page : cust1Page;
      const winnerPage = cust1Won ? cust1Page : cust2Page;
      await expect(loserPage.locator('.alert-danger')).toBeVisible();

      // Cancel the winner's order so the seat is freed cleanly
      const raceOrderId = Number(winnerPage.url().match(/\/orders\/(\d+)$/)![1]);
      await Promise.all([
        winnerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        winnerPage.click(`form[action="/orders/${raceOrderId}/cancel"] button[type="submit"]`),
      ]);
      await expect(winnerPage.locator('body')).toContainText('Đã hủy');

      // -----------------------------------------------------------------------
      // 6.1 CUSTOMER NEGATIVE: Failed mock payment -> FAILED, 0 tickets, seats freed
      // -----------------------------------------------------------------------
      await cust1Page.goto(`/showtimes/${showtimeAId}/seats`);
      await cust1Page.locator('.seat-box[data-bookable="true"]').nth(0).click();
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('#btnSubmit'),
      ]);
      const failedOrderId = Number(cust1Page.url().match(/\/orders\/(\d+)$/)![1]);
      await cust1Page.goto(`/orders/${failedOrderId}/checkout`);
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('#btnPayFailed'),
      ]);
      await expect(cust1Page).toHaveURL(new RegExp(`/orders/${failedOrderId}$`));
      await expect(cust1Page.locator('body')).toContainText('FAILED');

      // Verify /api/orders/{failedOrderId}/tickets returns 404 (no tickets issued)
      const failedTicketsResp = await cust1Session.context.request.get(`/api/orders/${failedOrderId}/tickets`);
      expect(failedTicketsResp.status()).toBe(404);

      // -----------------------------------------------------------------------
      // 6.1 CUSTOMER NEGATIVE: Expired order cannot be checked out
      // -----------------------------------------------------------------------
      await cust1Page.goto(`/showtimes/${showtimeAId}/seats`);
      await cust1Page.locator('.seat-box[data-bookable="true"]').nth(1).click();
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('#btnSubmit'),
      ]);
      const expiredOrderId = Number(cust1Page.url().match(/\/orders\/(\d+)$/)![1]);

      // Force expire order via Runner e2e-sql and run expire command
      runRunnerCommand(['e2e-sql', 'force-expire-order'], state, {
        BGP_E2E_ORDER_ID: String(expiredOrderId),
      });
      runRunnerCommand(['expire'], state);

      await cust1Page.goto(`/orders/${expiredOrderId}/checkout`);
      // Redirected back to /orders/{expiredOrderId} with error because order is EXPIRED
      await expect(cust1Page).toHaveURL(new RegExp(`/orders/${expiredOrderId}$`));
      await expect(cust1Page.locator('body')).toContainText(/EXPIRED|Hết hạn/);

      // -----------------------------------------------------------------------
      // 6.1 CUSTOMER NEGATIVE: Customer B cannot access Customer A's PAID order or QR
      // -----------------------------------------------------------------------
      // Customer 1 buys a ticket on Showtime A and Showtime B
      await cust1Page.goto(`/showtimes/${showtimeAId}/seats`);
      await cust1Page.locator('.seat-box[data-bookable="true"]').nth(2).click();
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('#btnSubmit'),
      ]);
      const paidOrderAId = Number(cust1Page.url().match(/\/orders\/(\d+)$/)![1]);
      await cust1Page.goto(`/orders/${paidOrderAId}/checkout`);
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('#btnPaySuccess'),
      ]);
      const qrImgA = cust1Page.locator('img[src*="/tickets/"][src*="/qr"]').first();
      const qrSrcA = (await qrImgA.getAttribute('src'))!;

      // Customer 2 tries to view Customer 1's order details & QR image
      const cust2ApiOrderResp = await cust2Session.context.request.get(`/api/orders/${paidOrderAId}`);
      expect(cust2ApiOrderResp.status()).toBe(404);

      const cust2QrResp = await cust2Session.context.request.get(qrSrcA);
      expect(cust2QrResp.status()).toBe(404);

      // Also buy a ticket on Showtime B (> 30m in future) to test OUTSIDE_WINDOW
      await cust1Page.goto(`/showtimes/${showtimeBId}/seats`);
      await cust1Page.locator('.seat-box[data-bookable="true"]').nth(0).click();
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('#btnSubmit'),
      ]);
      const paidOrderBId = Number(cust1Page.url().match(/\/orders\/(\d+)$/)![1]);
      await cust1Page.goto(`/orders/${paidOrderBId}/checkout`);
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('#btnPaySuccess'),
      ]);
      const qrSrcB = (await cust1Page.locator('img[src*="/tickets/"][src*="/qr"]').first().getAttribute('src'))!;

      const tmpDir = path.join(__dirname, '..', '.tmp');
      if (!fs.existsSync(tmpDir)) fs.mkdirSync(tmpDir, { recursive: true });
      const qrPathA = path.join(tmpDir, `neg-qr-a-${paidOrderAId}.png`);
      const qrPathB = path.join(tmpDir, `neg-qr-b-${paidOrderBId}.png`);
      fs.writeFileSync(qrPathA, await (await cust1Session.context.request.get(qrSrcA)).body());
      fs.writeFileSync(qrPathB, await (await cust1Session.context.request.get(qrSrcB)).body());

      // -----------------------------------------------------------------------
      // 6.3 MANAGER NEGATIVE: Cancel showtime with PAID tickets -> rejected
      // -----------------------------------------------------------------------
      await managerPage.goto(`/management/showtimes/${showtimeAId}`);
      // Button should be disabled ("Không thể hủy (Đã bán vé)")
      await expect(managerPage.locator('button:has-text("Không thể hủy")')).toBeDisabled();

      // -----------------------------------------------------------------------
      // 6.2 STAFF NEGATIVE: WRONG_SHOWTIME & OUTSIDE_WINDOW
      // -----------------------------------------------------------------------
      // Scan Ticket A while Staff selected Showtime B -> WRONG_SHOWTIME ("SAI SUẤT CHIẾU")
      await staffPage.goto(`/staff/check-in?showtimeId=${showtimeBId}`);
      await staffPage.locator('#qrFileInput').setInputFiles(qrPathA);
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('SAI SUẤT CHIẾU', { timeout: 15_000 });

      // Scan Ticket B while Staff selected Showtime B (starts in 240m -> outside window) -> OUTSIDE_WINDOW ("NGOÀI GIỜ SOÁT VÉ")
      await staffPage.goto(`/staff/check-in?showtimeId=${showtimeBId}`);
      await staffPage.locator('#qrFileInput').setInputFiles(qrPathB);
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('NGOÀI GIỜ SOÁT VÉ', { timeout: 15_000 });

      // -----------------------------------------------------------------------
      // 6.2b STAFF QR FILE SCANNER DETERMINISM & ERROR HANDLING (RC-01 Phase 1)
      // -----------------------------------------------------------------------
      // 1) Upload valid 1x1 white PNG (no QR code) -> must transition to "ẢNH QR LỖI" (never stuck at "Chờ quét...")
      const invalidPngPath = path.join(tmpDir, `invalid-non-qr-${Date.now()}.png`);
      const onePixelWhitePng = Buffer.from(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==',
        'base64'
      );
      fs.writeFileSync(invalidPngPath, onePixelWhitePng);
      await staffPage.locator('#qrFileInput').setInputFiles(invalidPngPath);
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('ẢNH QR LỖI', { timeout: 15_000 });
      await expect(staffPage.locator('#resultCard')).toContainText('Không tìm thấy mã QR hợp lệ');

      // 2) Upload corrupted binary file with .png extension -> must transition to "ẢNH QR LỖI"
      const corruptedImgPath = path.join(tmpDir, `corrupted-${Date.now()}.png`);
      fs.writeFileSync(corruptedImgPath, Buffer.from('not-a-valid-png-image-payload'));
      await staffPage.locator('#qrFileInput').setInputFiles(corruptedImgPath);
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('ẢNH QR LỖI', { timeout: 15_000 });

      // 3) Consecutive upload without page reload: upload qrPathA while on Showtime B -> "SAI SUẤT CHIẾU"
      await staffPage.locator('#qrFileInput').setInputFiles(qrPathA);
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('SAI SUẤT CHIẾU', { timeout: 15_000 });

      // 4) Change showtime dropdown to Showtime A -> UI resets to "Sẵn sàng quét"
      await staffPage.selectOption('#showtimeSelect', String(showtimeAId));
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('Sẵn sàng quét');

      // 5) Consecutive upload of Ticket A on matching Showtime A -> "HỢP LỆ" (ACCEPTED)
      await staffPage.locator('#qrFileInput').setInputFiles(qrPathA);
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('HỢP LỆ', { timeout: 15_000 });

      // 6) Immediate re-upload of the same Ticket A file without reloading -> "ĐÃ SỬ DỤNG" (ALREADY_USED)
      await staffPage.locator('#qrFileInput').setInputFiles(qrPathA);
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('ĐÃ SỬ DỤNG', { timeout: 15_000 });

      if (fs.existsSync(invalidPngPath)) fs.unlinkSync(invalidPngPath);
      if (fs.existsSync(corruptedImgPath)) fs.unlinkSync(corruptedImgPath);

      // -----------------------------------------------------------------------
      // 6.2 & 6.3 ROLE AUTHORIZATION & DYNAMIC ROLE REVOCATION
      // -----------------------------------------------------------------------
      // Customer blocked from /staff/check-in and /management
      await cust1Page.goto('/staff/check-in');
      await expect(cust1Page).toHaveURL(/\/account\/access-denied/);

      const custScanApi = await cust1Session.context.request.post('/api/staff/tickets/scan', {
        data: { qrToken: 'BGP1.dummy', expectedShowtimeId: showtimeAId },
      });
      expect(custScanApi.status()).toBe(403);

      // Staff blocked from /management
      await staffPage.goto('/management/movies');
      await expect(staffPage).toHaveURL(/\/account\/access-denied/);

      // Dynamic role revocation: revoke STAFF role in DB while Staff has active cookie
      runRunnerCommand(['e2e-sql', 'revoke-staff'], state);
      try {
        const revokedResp = await staffSession.context.request.post('/api/staff/tickets/scan', {
          data: { qrToken: 'BGP1.dummy', expectedShowtimeId: showtimeAId },
        });
        // OnValidatePrincipal rejects principal & signs out -> 401 Unauthorized
        expect([401, 403]).toContain(revokedResp.status());
      } finally {
        runRunnerCommand(['e2e-sql', 'restore-staff'], state);
      }

      // -----------------------------------------------------------------------
      // 6.4 SECURITY: CSRF enforcement on state-changing POST
      // -----------------------------------------------------------------------
      const csrfResp = await cust1Session.context.request.post('/api/bookings/reserve', {
        data: { showtimeId: showtimeAId, seatIds: [1] },
        // Intentionally omitting X-XSRF-TOKEN header while authenticated cookie is present
      });
      expect(csrfResp.status()).toBe(400);

      const sensitiveQrA = getOrderSensitiveQrArtifactsInMemory(paidOrderAId, state);
      const sensitiveQrB = getOrderSensitiveQrArtifactsInMemory(paidOrderBId, state);
      const combinedSensitiveQr = {
        rawTokens: [...sensitiveQrA.rawTokens, ...sensitiveQrB.rawTokens],
        cipherB64List: [...sensitiveQrA.cipherB64List, ...sensitiveQrB.cipherB64List],
      };

      if (fs.existsSync(qrPathA)) fs.unlinkSync(qrPathA);
      if (fs.existsSync(qrPathB)) fs.unlinkSync(qrPathB);

      // Check console logs for any secret or real 43-char QR token leaks
      for (const s of [managerSession, cust1Session, cust2Session, staffSession, guestSession]) {
        for (const m of s.consoleMessages) {
          assertNoSecretLeakInText(m, state, 'Browser Console', combinedSensitiveQr);
        }
      }
    } finally {
      await managerSession.context.close();
      await cust1Session.context.close();
      await cust2Session.context.close();
      await staffSession.context.close();
      await guestSession.context.close();
    }
  });
});
