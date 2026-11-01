import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { loadE2eState } from '../e2e-env';
import {
  assertNoSecretLeakInText,
  createRoleSession,
  formatVnDateOnly,
  formatVnDateTimeLocal,
  loginInBrowser,
} from './helpers';

test.describe('BE-05B Phase 2-5: Complete Real-Browser Journey (Manager -> Customer -> Staff -> Manager)', () => {
  test('Full operational lifecycle across isolated browser contexts', async ({ browser }) => {
    const state = loadE2eState();
    const baseURL = state.baseUrl;

    const managerSession = await createRoleSession(browser, baseURL);
    const customerSession = await createRoleSession(browser, baseURL);
    const staffSession = await createRoleSession(browser, baseURL);

    try {
      const managerPage = managerSession.page;
      const customerPage = customerSession.page;
      const staffPage = staffSession.page;

      // Auto-accept browser confirmation dialogs on Manager forms (publish movie, close/cancel showtime)
      managerPage.on('dialog', (dialog) => dialog.accept());

      // =========================================================================
      // PHASE 2: MANAGER — CREATE MOVIE -> EDIT -> PUBLISH -> PUBLISH SHOWTIME
      // =========================================================================
      await loginInBrowser(managerPage, 'manager@demo.invalid', state.demoPassword);

      // 2.1 Navigate to /management/movies and create a new movie in DRAFT
      await managerPage.goto('/management/movies');
      await expect(managerPage.locator('h1')).toContainText('Danh mục Phim');

      await managerPage.click('a[href*="/management/movies/create"]');
      await expect(managerPage).toHaveURL(/\/management\/movies\/create/);

      const uniqueSuffix = Date.now().toString().slice(-6);
      const draftTitle = `Phim E2E Giải Phóng ${uniqueSuffix} Draft`;
      const publishedTitle = `Phim E2E Giải Phóng ${uniqueSuffix} Official`;

      await managerPage.fill('input[name="Title"]', draftTitle);
      await managerPage.fill('input[name="DurationMin"]', '105');
      await managerPage.selectOption('select[name="AgeLabel"]', 'T13');
      await managerPage.fill('input[name="GenreText"]', 'Hành động, Khoa học viễn tưởng');
      await managerPage.fill('input[name="PosterUrl"]', 'https://example.com/posters/e2e-movie.jpg');
      await managerPage.fill('textarea[name="Synopsis"]', 'Bộ phim kiểm thử E2E trên trình duyệt thật cho hệ thống Beta Giải Phóng.');

      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click('main button[type="submit"]'),
      ]);

      // Verify redirect to MovieDetail (/management/movies/{id}) and status is DRAFT
      await expect(managerPage).toHaveURL(/\/management\/movies\/(\d+)$/);
      const movieUrlMatch = managerPage.url().match(/\/management\/movies\/(\d+)$/);
      expect(movieUrlMatch).not.toBeNull();
      const movieId = Number(movieUrlMatch![1]);

      await expect(managerPage.locator('body')).toContainText(draftTitle);
      await expect(managerPage.locator('.badge', { hasText: 'DRAFT' }).first()).toBeVisible();

      // Verify customer catalog does NOT display DRAFT movie
      await customerPage.goto('/movies');
      await expect(customerPage.locator('body')).not.toContainText(draftTitle);

      // 2.2 Edit movie metadata while in DRAFT
      await managerPage.goto(`/management/movies/${movieId}/edit`);
      await managerPage.fill('input[name="Title"]', publishedTitle);
      await managerPage.fill('input[name="DurationMin"]', '110');
      await managerPage.fill('textarea[name="Synopsis"]', 'Bản cập nhật chính thức sau khi biên tập nội dung phim E2E.');

      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click('main button[type="submit"]'),
      ]);

      await expect(managerPage).toHaveURL(new RegExp(`/management/movies/${movieId}$`));
      await expect(managerPage.locator('h1, h2, .h3')).toContainText(publishedTitle);
      await expect(managerPage.locator('body')).toContainText('110 phút');

      // 2.3 Publish movie (DRAFT -> PUBLISHED)
      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click(`form[action*="/management/movies/${movieId}/publish"] button[type="submit"]`),
      ]);

      await expect(managerPage.locator('.badge', { hasText: 'PUBLISHED' }).first()).toBeVisible();

      // 2.4 Publish a new Showtime at now + 15 minutes (in Vietnam time UTC+7)
      // Wait: StartUtc = now + 15 min is BOTH > now (open for booking) AND >= StartUtc - 30 min (inside Staff check-in window!)
      await managerPage.goto('/management/showtimes/create');
      await expect(managerPage.locator('select[name="MovieId"]')).toBeVisible();

      await managerPage.selectOption('select[name="MovieId"]', String(movieId));

      // Select the first active room
      const roomOption = managerPage.locator('select[name="RoomId"] option:not([value=""])').first();
      const roomId = Number(await roomOption.getAttribute('value'));
      const roomText = (await roomOption.textContent())?.trim() || '';
      await managerPage.selectOption('select[name="RoomId"]', String(roomId));

      const startUtcTarget = new Date(Date.now() + 15 * 60 * 1000);
      const startTimeVnStr = formatVnDateTimeLocal(startUtcTarget);
      const showtimeDateVn = formatVnDateOnly(startUtcTarget);

      await managerPage.fill('input[name="StartTimeVn"]', startTimeVnStr);
      await managerPage.fill('input[name="BufferMinutes"]', '15');

      const stdPriceInput = managerPage.locator('input[name="Prices[STANDARD]"]');
      if ((await stdPriceInput.count()) > 0) {
        await stdPriceInput.fill('75000');
      }
      const vipPriceInput = managerPage.locator('input[name="Prices[VIP]"]');
      if ((await vipPriceInput.count()) > 0) {
        await vipPriceInput.fill('95000');
      }

      await Promise.all([
        managerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        managerPage.click('main button[type="submit"]'),
      ]);

      // Verify redirect to /management/showtimes/{showtimeId}
      await expect(managerPage).toHaveURL(/\/management\/showtimes\/(\d+)$/);
      const showtimeUrlMatch = managerPage.url().match(/\/management\/showtimes\/(\d+)$/);
      expect(showtimeUrlMatch).not.toBeNull();
      const showtimeId = Number(showtimeUrlMatch![1]);

      await expect(managerPage.locator('body')).toContainText('ON_SALE');
      await expect(managerPage.locator('body')).toContainText(publishedTitle);
      await expect(managerPage.locator('body')).toContainText('75,000');
      await expect(managerPage.locator('body')).toContainText('95,000');

      // Record baseline dashboard metrics for this movie before Customer buys tickets
      await managerPage.goto(`/management/reports?fromDate=${showtimeDateVn}&toDate=${showtimeDateVn}&movieId=${movieId}&roomId=${roomId}`);
      await expect(managerPage.locator('body')).toContainText('Tổng Doanh thu Hợp lệ');
      await expect(managerPage.locator('body')).toContainText('0 VND');

      // =========================================================================
      // PHASE 3: CUSTOMER — BROWSE -> RESERVE 2 SEATS -> CHECKOUT -> MOCK PAY -> QR
      // =========================================================================
      await loginInBrowser(customerPage, 'customer1@demo.invalid', state.demoPassword);

      // 3.1 Browse catalog /movies and verify published movie is visible
      await customerPage.goto('/movies');
      await expect(customerPage.locator('body')).toContainText(publishedTitle);

      // Open movie details /movies/{movieId}
      await customerPage.goto(`/movies/${movieId}`);
      await expect(customerPage.locator('h1')).toContainText(publishedTitle);
      const showtimeLink = customerPage.locator(`a[href="/showtimes/${showtimeId}/seats"]`);
      await expect(showtimeLink).toBeVisible();

      // 3.2 Navigate to seat map /showtimes/{showtimeId}/seats
      await showtimeLink.click();
      await expect(customerPage).toHaveURL(new RegExp(`/showtimes/${showtimeId}/seats`));

      const availableSeats = customerPage.locator('.seat-box[data-bookable="true"]');
      const availableCountBefore = await availableSeats.count();
      expect(availableCountBefore).toBeGreaterThanOrEqual(2);

      // Pick 2 seats (e.g., first available seat and last available seat to test pricing)
      const seat1 = availableSeats.nth(0);
      const seat2 = availableSeats.nth(availableCountBefore - 1);

      const seat1Code = (await seat1.getAttribute('data-seat-code'))!;
      const seat2Code = (await seat2.getAttribute('data-seat-code'))!;
      const seat1Price = Number(await seat1.getAttribute('data-price'));
      const seat2Price = Number(await seat2.getAttribute('data-price'));
      const expectedTotalVnd = seat1Price + seat2Price;

      await seat1.click();
      await seat2.click();

      await expect(customerPage.locator('#selectedCount')).toHaveText('2');
      await expect(customerPage.locator('#selectedSeatsBadges')).toContainText(seat1Code);
      await expect(customerPage.locator('#selectedSeatsBadges')).toContainText(seat2Code);
      await expect(customerPage.locator('#btnSubmit')).toBeEnabled();

      // 3.3 Submit seat reservation
      await Promise.all([
        customerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        customerPage.click('#btnSubmit'),
      ]);

      // Verify redirect to /orders/{orderId}
      await expect(customerPage).toHaveURL(/\/orders\/(\d+)$/);
      const orderIdMatch = customerPage.url().match(/\/orders\/(\d+)$/);
      expect(orderIdMatch).not.toBeNull();
      const orderId = Number(orderIdMatch![1]);

      await expect(customerPage.locator('body')).toContainText('Chờ thanh toán');
      await expect(customerPage.locator('#countdownTimer')).toBeVisible();
      await expect(customerPage.locator('body')).toContainText(seat1Code);
      await expect(customerPage.locator('body')).toContainText(seat2Code);

      // 3.4 Proceed to Checkout (/orders/{orderId}/checkout)
      await customerPage.click(`a[href="/orders/${orderId}/checkout"]`);
      await expect(customerPage).toHaveURL(new RegExp(`/orders/${orderId}/checkout`));
      await expect(customerPage.locator('#countdownTimer')).toBeVisible();
      await expect(customerPage.locator('#btnPaySuccess')).toBeVisible();

      // 3.5 Execute simulated mock payment (SUCCESS)
      await Promise.all([
        customerPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        customerPage.click('#btnPaySuccess'),
      ]);

      // Verify redirect to /orders/{orderId}/tickets
      await expect(customerPage).toHaveURL(new RegExp(`/orders/${orderId}/tickets`));
      await expect(customerPage.locator('body')).toContainText('PAID');

      const ticketCards = customerPage.locator('.ticket-card');
      await expect(ticketCards).toHaveCount(2);

      // Verify QR images render with naturalWidth > 0 and valid PNG bytes
      const qrImages = customerPage.locator('img[src*="/tickets/"][src*="/qr"]');
      await expect(qrImages).toHaveCount(2);

      const firstQrSrc = (await qrImages.nth(0).getAttribute('src'))!;
      await expect
        .poll(
          async () =>
            await qrImages.nth(0).evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0),
          { timeout: 10_000 }
        )
        .toBe(true);

      // Download the first QR PNG via customer's authenticated context to pass to Staff's browser scanner
      const qrResp = await customerSession.context.request.get(firstQrSrc);
      expect(qrResp.status()).toBe(200);
      expect(qrResp.headers()['content-type']).toContain('image/png');
      const qrPngBuffer = await qrResp.body();
      // Verify PNG magic signature: 89 50 4E 47
      expect(qrPngBuffer[0]).toBe(0x89);
      expect(qrPngBuffer[1]).toBe(0x50);
      expect(qrPngBuffer[2]).toBe(0x4e);
      expect(qrPngBuffer[3]).toBe(0x47);

      const tmpDir = path.join(__dirname, '..', '.tmp');
      if (!fs.existsSync(tmpDir)) fs.mkdirSync(tmpDir, { recursive: true });
      const qrFilePath = path.join(tmpDir, `ticket-qr-${orderId}.png`);
      fs.writeFileSync(qrFilePath, qrPngBuffer);

      // 3.6 Reload tickets page to confirm persistence & check seat map shows SOLD
      await customerPage.reload({ waitUntil: 'domcontentloaded' });
      await expect(customerPage.locator('.ticket-card')).toHaveCount(2);
      await expect(customerPage.locator('body')).toContainText('HỢP LỆ / CHƯA SOÁT');

      await customerPage.goto(`/showtimes/${showtimeId}/seats`);
      const soldSeat1 = customerPage.locator(`.seat-box[data-seat-code="${seat1Code}"]`);
      await expect(soldSeat1).toHaveAttribute('data-bookable', 'false');
      await expect(soldSeat1).toHaveClass(/seat-sold/);

      // =========================================================================
      // PHASE 4: STAFF — IN-BROWSER QR DECODING & CHECK-IN (ACCEPTED -> ALREADY_USED)
      // =========================================================================
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);

      await staffPage.goto(`/staff/check-in?showtimeId=${showtimeId}`);
      await expect(staffPage.locator('#showtimeSelect')).toHaveValue(String(showtimeId));

      // 4.1 Upload the customer's QR PNG image into #qrFileInput so Html5Qrcode decodes it in-browser
      const fileInput = staffPage.locator('#qrFileInput');
      await fileInput.setInputFiles(qrFilePath);

      // Wait for #scanStatusBadge to transition to "HỢP LỆ" (ACCEPTED)
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('HỢP LỆ', { timeout: 15_000 });
      await expect(staffPage.locator('#ticketDetailsCard')).toBeVisible();
      const scannedTicketCode = (await staffPage.locator('#resTicketCode').textContent())?.trim() || '';
      expect(scannedTicketCode.length).toBeGreaterThan(5);
      await expect(staffPage.locator('#resMovieTitle')).toContainText(publishedTitle);

      // Verify Customer's ticket page now shows 1 ticket as USED and 1 as ISSUED
      await customerPage.goto(`/orders/${orderId}/tickets`);
      await expect(customerPage.locator('body')).toContainText('USED');

      // 4.2 Staff clicks #btnRescanCurrent ("Quét lại vé này") -> must return ALREADY_USED ("ĐÃ SỬ DỤNG")
      await expect(staffPage.locator('#btnRescanCurrent')).toBeVisible();
      await staffPage.click('#btnRescanCurrent');
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('ĐÃ SỬ DỤNG', { timeout: 15_000 });

      // 4.3 Verify /staff/check-in/history records both ACCEPTED and ALREADY_USED for this showtime
      await staffPage.goto(`/staff/check-in/history?showtimeId=${showtimeId}`);
      await expect(staffPage.locator('table')).toContainText('HỢP LỆ (ACCEPTED)');
      await expect(staffPage.locator('table')).toContainText('ĐÃ DÙNG (ALREADY_USED)');
      await expect(staffPage.locator('table')).toContainText(scannedTicketCode);

      // =========================================================================
      // PHASE 5: MANAGER — REVENUE, TICKETS SOLD & OCCUPANCY RECONCILIATION
      // =========================================================================
      await managerPage.goto(
        `/management/reports?fromDate=${showtimeDateVn}&toDate=${showtimeDateVn}&movieId=${movieId}&roomId=${roomId}`
      );

      const expectedRevenueFormatted = expectedTotalVnd.toLocaleString('en-US'); // e.g., 170,000
      const expectedOccupancyPct = ((2 / availableCountBefore) * 100).toFixed(2); // e.g., 4.17

      // Verify KPI cards
      await expect(managerPage.locator('body')).toContainText(`${expectedRevenueFormatted} VND`);
      await expect(managerPage.locator('body')).toContainText('2 vé');
      await expect(managerPage.locator('body')).toContainText(`${expectedOccupancyPct}%`);
      await expect(managerPage.locator('body')).toContainText('1 suất');

      // Verify Revenue by Movie table
      await expect(managerPage.locator('body')).toContainText(publishedTitle);
      await expect(managerPage.locator('body')).toContainText(`${expectedRevenueFormatted} đ`);

      // Verify Revenue by Day (VN) table
      await expect(managerPage.locator('body')).toContainText(showtimeDateVn);

      // Verify reloading /management/reports does not alter revenue or ticket counts
      await managerPage.reload({ waitUntil: 'domcontentloaded' });
      await expect(managerPage.locator('body')).toContainText(`${expectedRevenueFormatted} VND`);
      await expect(managerPage.locator('body')).toContainText('2 vé');

      // Verify no secret or raw QR token leaked into console logs of any session
      for (const session of [managerSession, customerSession, staffSession]) {
        for (const msg of session.consoleMessages) {
          assertNoSecretLeakInText(msg, state, 'Browser Console');
        }
        expect(session.pageErrors).toEqual([]);
      }
    } finally {
      await managerSession.context.close();
      await customerSession.context.close();
      await staffSession.context.close();
    }
  });
});
