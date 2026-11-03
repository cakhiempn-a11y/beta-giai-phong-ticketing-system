import { test, expect } from '@playwright/test';
import { loadE2eState, runRunnerCommandOrThrow } from '../e2e-env';
import { createRoleSession, formatVnDateTimeLocal, loginInBrowser } from './helpers';

test.describe('UI-P4: Multi-Cue Auditorium Seat Map & Full 2D Keyboard Accessibility (/showtimes/{id}/seats)', () => {
  let activeShowtimeId = 0;
  let closedShowtimeId = 0;
  let heldOrderIdToCleanup = 0;

  test.beforeAll(async ({ browser }) => {
    const state = loadE2eState();
    runRunnerCommandOrThrow(['seed-verified-showcase'], state);

    const mgrSession = await createRoleSession(browser, state.baseUrl);
    const cust1Session = await createRoleSession(browser, state.baseUrl);
    const cust2Session = await createRoleSession(browser, state.baseUrl);

    try {
      const mgrPage = mgrSession.page;
      const cust1Page = cust1Session.page;
      const cust2Page = cust2Session.page;

      mgrPage.on('dialog', (d) => d.accept());

      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);
      await loginInBrowser(cust1Page, 'customer1@demo.invalid', state.demoPassword);
      await loginInBrowser(cust2Page, 'customer2@demo.invalid', state.demoPassword);

      // Find verified film "Mê trong mê" (Charade, 1963)
      const moviesResp = await mgrPage.request.get('/api/movies');
      const movies = (await moviesResp.json()) as any[];
      const charade = movies.find((m) => m.provenance?.wikidataQid === 'Q496266') ?? movies[0];
      expect(charade).toBeTruthy();

      // Create dedicated 8-column PHONG_02 showtime (+6 days) for UI-P4 active tests
      await mgrPage.goto('/management/showtimes/create');
      await mgrPage.selectOption('select[name="MovieId"]', String(charade.movieId));

      const phong02Opt = mgrPage.locator('select[name="RoomId"] option', { hasText: 'Phòng 02' }).first();
      const targetRoomOpt =
        (await phong02Opt.count()) > 0
          ? phong02Opt
          : mgrPage.locator('select[name="RoomId"] option:not([value=""])').first();
      const roomId = (await targetRoomOpt.getAttribute('value'))!;
      await mgrPage.selectOption('select[name="RoomId"]', roomId);

      const startActive = new Date(Date.now() + 6 * 24 * 60 * 60 * 1000);
      await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(startActive));
      await mgrPage.fill('input[name="BufferMinutes"]', '15');
      await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
      await mgrPage.fill('input[name="Prices[VIP]"]', '95000');

      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      activeShowtimeId = Number(mgrPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);
      expect(activeShowtimeId).toBeGreaterThan(0);

      // Customer 1 buys seat B4 (SOLD state) on activeShowtimeId
      await cust1Page.goto(`/showtimes/${activeShowtimeId}/seats`);
      await cust1Page.locator('.seat-box[data-seat-code="B4"]').click();
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('#btnSubmit'),
      ]);
      const paidOrderId = Number(cust1Page.url().match(/\/orders\/(\d+)$/)![1]);
      await cust1Page.goto(`/orders/${paidOrderId}/checkout`);
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('#btnPaySuccess'),
      ]);

      // Customer 2 reserves seat C4 (HELD state) on activeShowtimeId so all 5 states coexist
      await cust2Page.goto(`/showtimes/${activeShowtimeId}/seats`);
      await cust2Page.locator('.seat-box[data-seat-code="C4"]').click();
      await Promise.all([
        cust2Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust2Page.click('#btnSubmit'),
      ]);
      heldOrderIdToCleanup = Number(cust2Page.url().match(/\/orders\/(\d+)$/)![1]);

      // Create second dedicated showtime (+7 days) and close it for closed-showtime verification
      await mgrPage.goto('/management/showtimes/create');
      await mgrPage.selectOption('select[name="MovieId"]', String(charade.movieId));
      await mgrPage.selectOption('select[name="RoomId"]', roomId);
      const startClosed = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000);
      await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(startClosed));
      await mgrPage.fill('input[name="BufferMinutes"]', '15');
      await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
      await mgrPage.fill('input[name="Prices[VIP]"]', '95000');
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      closedShowtimeId = Number(mgrPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);

      await mgrPage.click(`form[action*="/management/showtimes/${closedShowtimeId}/close"] button[type="submit"]`);
      await expect(mgrPage.locator('[data-testid="close-showtime-dialog"]')).toBeVisible();
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('[data-testid="confirm-close-showtime-btn"]'),
      ]);
      await expect(mgrPage.locator('body')).toContainText('CLOSED');
    } finally {
      await mgrSession.context.close();
      await cust1Session.context.close();
      await cust2Session.context.close();
    }
  });

  test.afterAll(async ({ browser }) => {
    if (!heldOrderIdToCleanup) return;
    const state = loadE2eState();
    const cust2Session = await createRoleSession(browser, state.baseUrl);
    try {
      const cust2Page = cust2Session.page;
      cust2Page.on('dialog', (d) => d.accept());
      await loginInBrowser(cust2Page, 'customer2@demo.invalid', state.demoPassword);
      await cust2Page.goto(`/orders/${heldOrderIdToCleanup}`);
      const cancelBtn = cust2Page.locator(`form[action="/orders/${heldOrderIdToCleanup}/cancel"] button[type="submit"]`);
      if ((await cancelBtn.count()) > 0 && (await cancelBtn.isVisible())) {
        await cancelBtn.click();
        await Promise.all([
          cust2Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          cust2Page.click('[data-testid="confirm-cancel-order-btn"]'),
        ]);
      }
    } finally {
      await cust2Session.context.close();
    }
  });

  test('11.1 Zero-Bootstrap isolation, verified poster/metadata, no premature countdown, and 5-state multi-cue grayscale accessibility', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const custSession = await createRoleSession(browser, state.baseUrl);
    try {
      const page = custSession.page;
      await page.setViewportSize({ width: 1440, height: 900 });
      await loginInBrowser(page, 'customer1@demo.invalid', state.demoPassword);

      await page.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });

      // 1. Zero-Bootstrap isolation & obsidian canvas (#0A0A0C -> rgb(10, 10, 12))
      const shellCheck = await page.evaluate(() => ({
        bg: window.getComputedStyle(document.body).backgroundColor,
        hasBootstrapCss: Array.from(document.querySelectorAll('link[rel="stylesheet"]')).some((l) =>
          (l.getAttribute('href') || '').includes('bootstrap')
        ),
        hasBootstrapJs: Array.from(document.querySelectorAll('script[src]')).some((s) =>
          (s.getAttribute('src') || '').includes('bootstrap')
        ),
        hasJQuery: Array.from(document.querySelectorAll('script[src]')).some((s) =>
          (s.getAttribute('src') || '').includes('jquery')
        ),
      }));
      expect(shellCheck.bg).toBe('rgb(10, 10, 12)');
      expect(shellCheck.hasBootstrapCss).toBe(false);
      expect(shellCheck.hasBootstrapJs).toBe(false);
      expect(shellCheck.hasJQuery).toBe(false);

      // 2. Verified movie poster + metadata + stepper + NO premature countdown timer
      await expect(page.locator('[data-testid="booking-stepper"]')).toBeVisible();
      await expect(page.locator('#auditorium-heading')).toHaveText('Mê trong mê');
      await expect(page.locator('[data-testid="summary-rating-badge"]')).toContainText('IMDb 7.8/10');
      const posterImg = page.locator('[data-testid="summary-poster-img"]');
      await expect(posterImg).toBeVisible();
      expect(await posterImg.getAttribute('srcset')).toContain('342w');

      await expect(page.locator('[data-testid="seat-hold-honesty-note"]')).toContainText(
        'Chưa tính giờ giữ chỗ ở bước này'
      );
      await expect(page.locator('#countdownTimer')).toHaveCount(0);

      // 3. Select C5 (VIP) so all 5 states (Standard •, VIP ★, Selected ✓, Held ⏳, Sold ✕) are visible simultaneously
      await page.locator('.seat-box[data-seat-code="C5"]').click();

      // Apply full grayscale(1) filter to prove non-color multi-cue differentiation
      await page.evaluate(() => {
        document.documentElement.style.filter = 'grayscale(1)';
      });

      const multiCueAudit = await page.evaluate(() => {
        function inspectSeat(code: string) {
          const el = document.querySelector(`.seat-box[data-seat-code="${code}"]`) as HTMLElement;
          const style = window.getComputedStyle(el);
          const cue = el.querySelector('[data-seat-cue]')?.textContent?.trim() || '';
          const codeStyle = window.getComputedStyle(el.querySelector('.bgp-seat__code')!);
          return {
            tag: el.tagName,
            cue,
            borderStyle: style.borderTopStyle,
            bgImage: style.backgroundImage,
            boxShadow: style.boxShadow,
            textDecoration: codeStyle.textDecorationLine,
            ariaPressed: el.getAttribute('aria-pressed'),
            ariaDisabled: el.getAttribute('aria-disabled'),
          };
        }

        return {
          std: inspectSeat('A1'),
          vip: inspectSeat('C3'),
          selected: inspectSeat('C5'),
          held: inspectSeat('C4'),
          sold: inspectSeat('B4'),
        };
      });

      expect(multiCueAudit.std.tag).toBe('BUTTON');
      expect(multiCueAudit.std.cue).toBe('•');
      expect(multiCueAudit.std.ariaPressed).toBe('false');

      expect(multiCueAudit.vip.cue).toBe('★');
      expect(multiCueAudit.vip.boxShadow).toContain('inset');
      expect(multiCueAudit.vip.ariaPressed).toBe('false');

      expect(multiCueAudit.selected.cue).toBe('✓');
      expect(multiCueAudit.selected.ariaPressed).toBe('true');

      expect(multiCueAudit.held.cue).toBe('⏳');
      expect(multiCueAudit.held.borderStyle).toBe('dashed');
      expect(multiCueAudit.held.bgImage).toContain('repeating-linear-gradient');
      expect(multiCueAudit.held.ariaDisabled).toBe('true');

      expect(multiCueAudit.sold.cue).toBe('✕');
      expect(multiCueAudit.sold.textDecoration).toContain('line-through');
      expect(multiCueAudit.sold.ariaDisabled).toBe('true');
    } finally {
      await custSession.context.close();
    }
  });

  test('11.2 100% Keyboard-only 2D Roving Tabindex seat navigation, selection & reservation', async ({ browser }) => {
    const state = loadE2eState();
    const custSession = await createRoleSession(browser, state.baseUrl);
    try {
      const page = custSession.page;
      page.on('dialog', (d) => d.accept());
      await loginInBrowser(page, 'customer1@demo.invalid', state.demoPassword);

      await page.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });

      // Exactly 1 seat has tabindex="0" (Roving Tabindex entry point: A1)
      await expect(page.locator('.seat-box[tabindex="0"]')).toHaveCount(1);
      await expect(page.locator('.seat-box[tabindex="0"]')).toHaveAttribute('data-seat-code', 'A1');

      // Focus roving entry point A1, move right to A2, press Space to select Standard seat A2 (75,000 đ)
      await page.locator('.seat-box[tabindex="0"]').focus();
      await page.keyboard.press('ArrowRight');
      await expect(page.locator(':focus')).toHaveAttribute('data-seat-code', 'A2');
      await page.keyboard.press('Space');
      await expect(page.locator('.seat-box[data-seat-code="A2"]')).toHaveAttribute('aria-pressed', 'true');
      await expect(page.locator('#selectedCount')).toHaveText('1');

      // Navigate down 2 rows (A2 -> B2 -> C2 VIP) and right to C3, then press Enter to select VIP seat C3 (95,000 đ)
      await page.keyboard.press('ArrowDown');
      await expect(page.locator(':focus')).toHaveAttribute('data-seat-code', 'B2');
      await page.keyboard.press('ArrowDown');
      await expect(page.locator(':focus')).toHaveAttribute('data-seat-code', 'C2');
      await page.keyboard.press('ArrowRight');
      await expect(page.locator(':focus')).toHaveAttribute('data-seat-code', 'C3');

      // Also verify End / Home within row C
      await page.keyboard.press('End');
      await expect(page.locator(':focus')).toHaveAttribute('data-seat-code', 'C8');
      await page.keyboard.press('Home');
      await expect(page.locator(':focus')).toHaveAttribute('data-seat-code', 'C1');
      await page.keyboard.press('ArrowRight');
      await page.keyboard.press('ArrowRight');
      await expect(page.locator(':focus')).toHaveAttribute('data-seat-code', 'C3');

      await page.keyboard.press('Enter');
      await expect(page.locator('.seat-box[data-seat-code="C3"]')).toHaveAttribute('aria-pressed', 'true');
      await expect(page.locator('#selectedCount')).toHaveText('2');
      await expect(page.locator('#selectedTotal')).toHaveText(/170[.,]000\s*đ/);

      // Traverse over SOLD seat B4 via keyboard without getting trapped, verifying pressing Space on SOLD seat does not select it
      await page.keyboard.press('ArrowUp');
      await page.keyboard.press('ArrowRight');
      await expect(page.locator(':focus')).toHaveAttribute('data-seat-code', 'B4');
      await page.keyboard.press('Space');
      await expect(page.locator('#selectedCount')).toHaveText('2');

      // Submit reservation using keyboard on #btnSubmit
      await page.locator('#btnSubmit').focus();
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.keyboard.press('Enter'),
      ]);

      await expect(page).toHaveURL(/\/orders\/(\d+)$/);
      const orderId = Number(page.url().match(/\/orders\/(\d+)$/)![1]);

      // Clean up temporary order so seats A2 & C3 return to AVAILABLE
      await page.click(`form[action="/orders/${orderId}/cancel"] button[type="submit"]`);
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('[data-testid="confirm-cancel-order-btn"]'),
      ]);
    } finally {
      await custSession.context.close();
    }
  });

  test('11.3 Max-8 seat guard triggers inline live banner with ZERO window.alert() dialogs', async ({ browser }) => {
    const state = loadE2eState();
    const custSession = await createRoleSession(browser, state.baseUrl);
    try {
      const page = custSession.page;
      const alertMessages: string[] = [];
      page.on('dialog', async (dialog) => {
        alertMessages.push(dialog.message());
        await dialog.dismiss();
      });

      await loginInBrowser(page, 'customer1@demo.invalid', state.demoPassword);
      await page.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });

      const limitBanner = page.locator('[data-testid="seat-limit-banner"]');
      await expect(limitBanner).toBeHidden();

      const bookable = page.locator('.seat-box[data-bookable="true"]');
      for (let i = 0; i < 8; i++) {
        await bookable.nth(i).click();
      }
      await expect(page.locator('#selectedCount')).toHaveText('8');
      await expect(limitBanner).toBeHidden();

      // Click 9th bookable seat -> must NOT trigger window.alert, must show inline banner naming the 9th seat
      const ninthSeat = bookable.nth(8);
      const ninthCode = (await ninthSeat.getAttribute('data-seat-code'))!;
      await ninthSeat.click();

      expect(alertMessages).toEqual([]);
      await expect(page.locator('#selectedCount')).toHaveText('8');
      await expect(ninthSeat).toHaveAttribute('aria-pressed', 'false');
      await expect(limitBanner).toBeVisible();
      await expect(limitBanner).toContainText(ninthCode);

      // Removing 1 seat via quick-remove chip button hides the limit banner automatically
      await page.locator('#selectedSeatsBadges .bgp-selected-item__remove').first().click();
      await expect(page.locator('#selectedCount')).toHaveText('7');
      await expect(limitBanner).toBeHidden();
    } finally {
      await custSession.context.close();
    }
  });

  test('11.4 Guest returnUrl + sessionStorage draft restoration, Operational role lock, Closed showtime & 320px-1440px responsive fit-zoom', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const guestSession = await createRoleSession(browser, state.baseUrl);
    const staffSession = await createRoleSession(browser, state.baseUrl);

    try {
      const guestPage = guestSession.page;
      const staffPage = staffSession.page;

      // A. Guest selects D5 & D6 -> clicks #btnSubmit -> redirected to /account/login?returnUrl=... -> logs in -> seats restored!
      await guestPage.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await expect(guestPage.locator('#btnSubmit')).toContainText('Đăng nhập để giữ ghế');

      await guestPage.locator('.seat-box[data-seat-code="D5"]').click();
      await guestPage.locator('.seat-box[data-seat-code="D6"]').click();
      await expect(guestPage.locator('#selectedCount')).toHaveText('2');
      await expect(guestPage.locator('#btnSubmit')).toContainText('Đăng nhập để giữ 2 ghế');

      await Promise.all([
        guestPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        guestPage.click('#btnSubmit'),
      ]);
      await expect(guestPage).toHaveURL(
        new RegExp(`/account/login\\?returnUrl=.*showtimes.*${activeShowtimeId}.*seats`, 'i')
      );

      await guestPage.fill('input[name="Request.Email"]', 'customer1@demo.invalid');
      await guestPage.fill('input[name="Request.Password"]', state.demoPassword);
      await Promise.all([
        guestPage.waitForNavigation({ waitUntil: 'networkidle' }),
        guestPage.click('button[type="submit"]'),
      ]);

      await expect(guestPage).toHaveURL(new RegExp(`/showtimes/${activeShowtimeId}/seats$`));
      await expect(guestPage.locator('[data-testid="seat-draft-restored-banner"]')).toBeVisible();
      await expect(guestPage.locator('#selectedCount')).toHaveText('2');
      await expect(guestPage.locator('.seat-box[data-seat-code="D5"]')).toHaveAttribute('aria-pressed', 'true');
      await expect(guestPage.locator('.seat-box[data-seat-code="D6"]')).toHaveAttribute('aria-pressed', 'true');
      await expect(guestPage.locator('#btnSubmit')).toBeEnabled();
      await expect(guestPage.locator('#btnSubmit')).toContainText('Giữ 2 ghế & Tiếp tục thanh toán');

      // Reloading proves one-shot draft consumption (no sticky stale draft on subsequent visits)
      await guestPage.reload({ waitUntil: 'networkidle' });
      await expect(guestPage.locator('#selectedCount')).toHaveText('0');

      // B. Operational role (STAFF) sees locked view-only notice and seats/submit are semantically disabled
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);
      await staffPage.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await expect(staffPage.locator('[data-testid="seat-operational-lock-notice"]')).toBeVisible();
      await expect(staffPage.locator('#btnSubmit')).toBeDisabled();
      await expect(staffPage.locator('.seat-box[data-seat-code="A1"]')).toBeDisabled();
      await staffPage.locator('.seat-box[data-seat-code="A1"]').click({ force: true });
      await expect(staffPage.locator('#selectedCount')).toHaveText('0');

      // C. Closed showtime displays honest closed banner & back-to-showtimes CTA with 0 bookable seats
      await guestPage.goto(`/showtimes/${closedShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await expect(guestPage.locator('[data-testid="seat-closed-banner"]')).toBeVisible();
      await expect(guestPage.locator('[data-testid="seat-closed-summary"]')).toBeVisible();
      await expect(guestPage.locator('[data-testid="seat-back-to-showtimes-cta"]')).toBeVisible();
      await expect(guestPage.locator('.seat-box[data-bookable="true"]')).toHaveCount(0);

      // D. Responsive viewports (320px, 390px, 768px, 1440px): zero document horizontal overflow + fit-to-screen zoom toggle
      await guestPage.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });
      for (const vp of [
        { width: 320, height: 568 },
        { width: 390, height: 844 },
        { width: 768, height: 1024 },
        { width: 1440, height: 900 },
      ]) {
        await guestPage.setViewportSize(vp);
        await guestPage.waitForTimeout(100);
        const overflow = await guestPage.evaluate(() => ({
          docScroll: document.documentElement.scrollWidth,
          docClient: document.documentElement.clientWidth,
          bodyScroll: document.body.scrollWidth,
          bodyClient: document.body.clientWidth,
        }));
        expect(
          overflow.docScroll <= overflow.docClient,
          `Doc overflow at ${vp.width}px: ${overflow.docScroll} > ${overflow.docClient}`
        ).toBe(true);
        expect(
          overflow.bodyScroll <= overflow.bodyClient,
          `Body overflow at ${vp.width}px: ${overflow.bodyScroll} > ${overflow.bodyClient}`
        ).toBe(true);
      }

      // Verify mobile fit-to-screen zoom toggle at 390x844
      await guestPage.setViewportSize({ width: 390, height: 844 });
      const zoomBtn = guestPage.locator('[data-testid="seat-zoom-toggle"]');
      await expect(zoomBtn).toBeVisible();
      await expect(zoomBtn).toHaveAttribute('aria-pressed', 'false');
      await zoomBtn.click();
      await expect(zoomBtn).toHaveAttribute('aria-pressed', 'true');
      await expect(guestPage.locator('[data-auditorium-viewport]')).toHaveClass(/is-fit-mode/);
    } finally {
      await guestSession.context.close();
      await staffSession.context.close();
    }
  });
});
