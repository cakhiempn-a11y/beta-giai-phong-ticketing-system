import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { loadE2eState, runRunnerCommandOrThrow } from '../e2e-env';
import {
  assertNoSecretLeakInText,
  createRoleSession,
  formatVnDateTimeLocal,
  getOrderSensitiveQrArtifactsInMemory,
  loginInBrowser,
} from './helpers';

async function assertZeroBootstrapAndNoBootstrapBlue(page: Page) {
  const audit = await page.evaluate(() => {
    const bodyBg = window.getComputedStyle(document.body).backgroundColor;
    const links = Array.from(document.querySelectorAll('link[rel="stylesheet"]')).map(
      (l) => l.getAttribute('href') || ''
    );
    const scripts = Array.from(document.querySelectorAll('script[src]')).map(
      (s) => s.getAttribute('src') || ''
    );

    let hasBootstrapBlue = false;
    for (const el of Array.from(document.querySelectorAll('body *'))) {
      const st = window.getComputedStyle(el);
      if (
        st.backgroundColor === 'rgb(13, 110, 253)' ||
        st.color === 'rgb(13, 110, 253)' ||
        st.borderTopColor === 'rgb(13, 110, 253)'
      ) {
        hasBootstrapBlue = true;
        break;
      }
    }

    return {
      bodyBg,
      hasBootstrapCss: links.some((h) => h.includes('bootstrap')),
      hasDarkCompatCss: links.some((h) => h.includes('dark-compat')),
      hasBootstrapJs: scripts.some((s) => s.includes('bootstrap')),
      hasJQuery: scripts.some((s) => s.includes('jquery')),
      hasWindowJQuery: typeof (window as any).jQuery !== 'undefined',
      hasBootstrapBlue,
    };
  });

  expect(audit.bodyBg).toBe('rgb(10, 10, 12)');
  expect(audit.hasBootstrapCss).toBe(false);
  expect(audit.hasDarkCompatCss).toBe(false);
  expect(audit.hasBootstrapJs).toBe(false);
  expect(audit.hasJQuery).toBe(false);
  expect(audit.hasWindowJQuery).toBe(false);
  expect(audit.hasBootstrapBlue).toBe(false);
}

test.describe('UI-P6: High-Contrast QR Boarding Passes, Order History & Cinema Identity (/orders/{id}/tickets, /orders, /account/*)', () => {
  let showtimeId = 0;

  test.beforeAll(async ({ browser }) => {
    const state = loadE2eState();
    runRunnerCommandOrThrow(['seed-verified-showcase'], state);
    runRunnerCommandOrThrow(['e2e-sql', 'seed-empty-customer'], state);

    const mgrSession = await createRoleSession(browser, state.baseUrl);
    try {
      const mgrPage = mgrSession.page;
      mgrPage.on('dialog', (d) => d.accept());
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);

      const moviesResp = await mgrPage.request.get('/api/movies');
      const movies = (await moviesResp.json()) as any[];
      const charade = movies.find((m) => m.provenance?.wikidataQid === 'Q496266') ?? movies[0];
      expect(charade).toBeTruthy();

      // Publish an ON_SALE showtime at now + 18 minutes in Phòng 02 (or Phòng 03 fallback on retry).
      // StartUtc = now + 18m is BOTH > now (bookable) AND >= StartUtc - 30m (inside Staff check-in window).
      const candidateRooms = ['Phòng 02', 'Phòng 03'];
      for (const roomLabel of candidateRooms) {
        await mgrPage.goto('/management/showtimes/create');
        await mgrPage.selectOption('select[name="MovieId"]', String(charade.movieId));

        const roomOpt = mgrPage.locator('select[name="RoomId"] option', { hasText: roomLabel }).first();
        if ((await roomOpt.count()) === 0) continue;
        const roomId = (await roomOpt.getAttribute('value'))!;
        await mgrPage.selectOption('select[name="RoomId"]', roomId);

        const startUtcTarget = new Date(Date.now() + 18 * 60 * 1000);
        await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(startUtcTarget));
        await mgrPage.fill('input[name="BufferMinutes"]', '15');
        await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
        await mgrPage.fill('input[name="Prices[VIP]"]', '95000');

        await Promise.all([
          mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          mgrPage.click('main button[type="submit"]'),
        ]);

        const match = mgrPage.url().match(/\/management\/showtimes\/(\d+)$/);
        if (match) {
          showtimeId = Number(match[1]);
          break;
        }
      }

      expect(showtimeId).toBeGreaterThan(0);
    } finally {
      await mgrSession.context.close();
    }
  });

  test('13.1 High-contrast QR boarding passes (/orders/{id}/tickets), real rendered optical scan (ISSUED -> USED), @media print & zero secret leaks', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const custSession = await createRoleSession(browser, state.baseUrl);
    const staffSession = await createRoleSession(browser, state.baseUrl);
    const tmpDir = path.join(__dirname, '..', '.tmp');
    if (!fs.existsSync(tmpDir)) fs.mkdirSync(tmpDir, { recursive: true });
    let renderedQrPath = '';

    try {
      const custPage = custSession.page;
      const staffPage = staffSession.page;
      await custPage.setViewportSize({ width: 1440, height: 900 });
      await loginInBrowser(custPage, 'customer1@demo.invalid', state.demoPassword);

      // Reserve A2 (Standard 75k) + C5 (VIP 95k) = 170,000 VND -> Pay SUCCESS
      await custPage.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await custPage.locator('.seat-box[data-seat-code="A2"]').click();
      await custPage.locator('.seat-box[data-seat-code="C5"]').click();
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnSubmit'),
      ]);

      const orderId = Number(custPage.url().match(/\/orders\/(\d+)$/)![1]);
      await custPage.goto(`/orders/${orderId}/checkout`, { waitUntil: 'networkidle' });
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnPaySuccess'),
      ]);

      await expect(custPage).toHaveURL(new RegExp(`/orders/${orderId}/tickets$`));
      await custPage.waitForLoadState('networkidle');

      // 1. Verify Zero-Bootstrap isolation & flash banner
      await assertZeroBootstrapAndNoBootstrapBlue(custPage);
      await expect(custPage.locator('[data-testid="order-tickets-page"]')).toBeVisible();
      await expect(custPage.locator('[data-testid="tickets-flash-success"]')).toBeVisible();

      // 2. Verify Screening & Film Dossier Header (real verified poster, IMDb rating, UTC+7 timestamps)
      const dossier = custPage.locator('[data-testid="tickets-dossier-header"]');
      await expect(dossier).toBeVisible();
      await expect(dossier).toContainText('PAID');
      await expect(custPage.locator('[data-testid="tickets-movie-rating"]')).toContainText('IMDb 7.8/10');

      const dossierPoster = dossier.locator('img.bgp-tk-dossier__poster');
      await expect(dossierPoster).toBeVisible();
      expect(await dossierPoster.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBeGreaterThan(0);
      expect(await dossierPoster.getAttribute('srcset')).toContain('342w');

      const mainText = await custPage.locator('main').innerText();
      expect(mainText).toContain('UTC+7');
      expect(mainText).not.toMatch(/\d{2}:\d{2}\s+UTC\b/);

      // 3. Verify 2 perforated boarding pass cards in ISSUED state + Hard-Locked #FFFFFF Optical QR Quiet Zone
      const ticketCards = custPage.locator('.ticket-card');
      await expect(ticketCards).toHaveCount(2);
      await expect(custPage.locator('[data-testid="ticket-status-issued"]')).toHaveCount(2);

      const qrBoxes = custPage.locator('[data-testid="ticket-qr-box"]');
      await expect(qrBoxes).toHaveCount(2);

      const qrAudit = await qrBoxes.first().evaluate((box) => {
        const boxStyle = window.getComputedStyle(box);
        const img = box.querySelector('img') as HTMLImageElement;
        const imgStyle = window.getComputedStyle(img);
        const rect = img.getBoundingClientRect();
        return {
          boxBg: boxStyle.backgroundColor,
          padTop: parseFloat(boxStyle.paddingTop),
          padRight: parseFloat(boxStyle.paddingRight),
          padBottom: parseFloat(boxStyle.paddingBottom),
          padLeft: parseFloat(boxStyle.paddingLeft),
          boxFilter: boxStyle.filter,
          boxOpacity: boxStyle.opacity,
          imgComplete: img.complete,
          imgNaturalWidth: img.naturalWidth,
          imgWidth: rect.width,
          imgHeight: rect.height,
          imgFilter: imgStyle.filter,
          imgOpacity: imgStyle.opacity,
        };
      });

      expect(qrAudit.boxBg).toBe('rgb(255, 255, 255)');
      expect(qrAudit.padTop).toBeGreaterThanOrEqual(12);
      expect(qrAudit.padRight).toBeGreaterThanOrEqual(12);
      expect(qrAudit.padBottom).toBeGreaterThanOrEqual(12);
      expect(qrAudit.padLeft).toBeGreaterThanOrEqual(12);
      expect(qrAudit.boxFilter).toBe('none');
      expect(qrAudit.boxOpacity).toBe('1');
      expect(qrAudit.imgComplete).toBe(true);
      expect(qrAudit.imgNaturalWidth).toBeGreaterThan(0);
      expect(qrAudit.imgWidth).toBeGreaterThanOrEqual(120);
      expect(qrAudit.imgHeight).toBeGreaterThanOrEqual(120);
      expect(qrAudit.imgFilter).toBe('none');
      expect(qrAudit.imgOpacity).toBe('1');

      // 4. Capture REAL rendered DOM pixels of .bgp-qr-box and scan via Staff Check-In Scanner
      renderedQrPath = path.join(tmpDir, `rendered-qr-box-order-${orderId}.png`);
      await qrBoxes.first().screenshot({ path: renderedQrPath });

      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);
      await staffPage.goto(`/staff/check-in?showtimeId=${showtimeId}`);
      await expect(staffPage.locator('#showtimeSelect')).toHaveValue(String(showtimeId));

      await staffPage.locator('#qrFileInput').setInputFiles(renderedQrPath);
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('HỢP LỆ', { timeout: 15_000 });
      await expect(staffPage.locator('#ticketDetailsCard')).toBeVisible();

      // 5. Reload Customer /orders/{id}/tickets -> verify clear visual distinction between USED (1) and ISSUED (1)
      await custPage.reload({ waitUntil: 'networkidle' });
      await expect(custPage.locator('[data-testid="ticket-status-used"]')).toHaveCount(1);
      await expect(custPage.locator('[data-testid="ticket-status-used"]')).toContainText('ĐÃ SOÁT VÉ (USED)');
      await expect(custPage.locator('[data-testid="ticket-used-timestamp"]')).toHaveCount(1);
      await expect(custPage.locator('[data-testid="ticket-used-timestamp"]')).toContainText('UTC+7');
      await expect(custPage.locator('[data-testid="ticket-status-issued"]')).toHaveCount(1);

      // 6. Emulate @media print and verify clean black-on-white boarding pass layout
      await custPage.emulateMedia({ media: 'print' });
      const printAudit = await custPage.evaluate(() => {
        const header = document.querySelector('.bgp-cinema-header');
        const footer = document.querySelector('.bgp-cinema-footer');
        const printBtn = document.querySelector('[data-testid="print-tickets-btn"]');
        const bodyStyle = window.getComputedStyle(document.body);
        const qrBox = document.querySelector('[data-testid="ticket-qr-box"]')!;
        const qrBoxStyle = window.getComputedStyle(qrBox);
        const cardStyle = window.getComputedStyle(document.querySelector('.ticket-card')!);
        return {
          headerDisplay: header ? window.getComputedStyle(header).display : 'none',
          footerDisplay: footer ? window.getComputedStyle(footer).display : 'none',
          printBtnDisplay: printBtn ? window.getComputedStyle(printBtn).display : 'none',
          bodyBg: bodyStyle.backgroundColor,
          bodyColor: bodyStyle.color,
          qrBoxBg: qrBoxStyle.backgroundColor,
          qrBoxFilter: qrBoxStyle.filter,
          cardBreakInside: cardStyle.breakInside,
        };
      });

      expect(printAudit.headerDisplay).toBe('none');
      expect(printAudit.footerDisplay).toBe('none');
      expect(printAudit.printBtnDisplay).toBe('none');
      expect(printAudit.bodyBg).toBe('rgb(255, 255, 255)');
      expect(printAudit.bodyColor).toBe('rgb(9, 9, 11)');
      expect(printAudit.qrBoxBg).toBe('rgb(255, 255, 255)');
      expect(printAudit.qrBoxFilter).toBe('none');
      expect(printAudit.cardBreakInside).toBe('avoid');
      await custPage.emulateMedia({ media: 'screen' });

      // 7. Verify zero raw QR token / HMAC secret leaks in HTML & Browser Console
      const sensitiveQr = getOrderSensitiveQrArtifactsInMemory(orderId, state);
      assertNoSecretLeakInText(await custPage.content(), state, 'UI-P6 Tickets HTML', sensitiveQr);
      for (const session of [custSession, staffSession]) {
        for (const msg of session.consoleMessages) {
          assertNoSecretLeakInText(msg, state, 'Browser Console', sensitiveQr);
        }
        expect(session.pageErrors).toEqual([]);
      }
    } finally {
      if (renderedQrPath && fs.existsSync(renderedQrPath)) {
        fs.unlinkSync(renderedQrPath);
      }
      await custSession.context.close();
      await staffSession.context.close();
    }
  });

  test('13.2 Order History (/orders) — verified posters, UTC+7 metadata, status filters (?status=...) & empty states', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const cust1Session = await createRoleSession(browser, state.baseUrl);
    const cust3Session = await createRoleSession(browser, state.baseUrl);

    try {
      const cust1Page = cust1Session.page;
      await cust1Page.setViewportSize({ width: 1440, height: 900 });
      await loginInBrowser(cust1Page, 'customer1@demo.invalid', state.demoPassword);

      // Ensure customer1 also has a CANCELLED order so both ?status=paid and ?status=inactive return entries
      await cust1Page.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await cust1Page.locator('.seat-box[data-seat-code="B3"]').click();
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('#btnSubmit'),
      ]);
      await cust1Page.click('#btnCancel');
      await cust1Page.locator('[data-testid="cancel-order-dialog"]').waitFor({ state: 'visible' });
      await Promise.all([
        cust1Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust1Page.click('[data-testid="confirm-cancel-order-btn"]'),
      ]);

      // Clean up any leftover active holds from earlier specs by expiring due holds
      runRunnerCommandOrThrow(['expire'], state);

      // 1. Visit /orders (All orders)
      await cust1Page.goto('/orders', { waitUntil: 'networkidle' });
      await assertZeroBootstrapAndNoBootstrapBlue(cust1Page);
      await expect(cust1Page.locator('[data-testid="orders-history-page"]')).toBeVisible();

      const cards = cust1Page.locator('[data-testid="order-list-card"]');
      expect(await cards.count()).toBeGreaterThanOrEqual(2);

      // Verify poster, UTC+7 timestamp, seat codes, and Xem vé QR CTA on PAID order card
      const paidCard = cust1Page.locator('[data-testid="order-list-card"][data-order-status="PAID"]').first();
      await expect(paidCard).toBeVisible();
      const cardPoster = paidCard.locator('img.bgp-order-card__poster');
      expect(await cardPoster.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBeGreaterThan(0);
      await expect(paidCard).toContainText('UTC+7');
      await expect(paidCard.locator('[data-testid="order-card-tickets-cta"]')).toBeVisible();

      // 2. Filter ?status=paid
      await cust1Page.goto('/orders?status=paid', { waitUntil: 'networkidle' });
      const paidFilteredCards = cust1Page.locator('[data-testid="order-list-card"]');
      const paidCount = await paidFilteredCards.count();
      expect(paidCount).toBeGreaterThanOrEqual(1);
      for (let i = 0; i < paidCount; i++) {
        await expect(paidFilteredCards.nth(i)).toHaveAttribute('data-order-status', 'PAID');
      }

      // 3. Filter ?status=inactive
      await cust1Page.goto('/orders?status=inactive', { waitUntil: 'networkidle' });
      const inactiveCards = cust1Page.locator('[data-testid="order-list-card"]');
      const inactiveCount = await inactiveCards.count();
      expect(inactiveCount).toBeGreaterThanOrEqual(1);
      for (let i = 0; i < inactiveCount; i++) {
        const statusAttr = await inactiveCards.nth(i).getAttribute('data-order-status');
        expect(['CANCELLED', 'EXPIRED', 'FAILED', 'NEEDS_REVIEW', 'PENDING_PAYMENT']).toContain(statusAttr);
      }

      // 4. True 0-orders empty state via fresh customer3@demo.invalid
      const cust3Page = cust3Session.page;
      await cust3Page.setViewportSize({ width: 1440, height: 900 });
      await loginInBrowser(cust3Page, 'customer3@demo.invalid', state.demoPassword);
      await cust3Page.goto('/orders', { waitUntil: 'networkidle' });
      await assertZeroBootstrapAndNoBootstrapBlue(cust3Page);
      await expect(cust3Page.locator('[data-testid="orders-empty-state"]')).toBeVisible();
      await expect(cust3Page.locator('[data-testid="orders-empty-state"]')).toContainText(
        'Bạn chưa có đơn đặt vé nào'
      );

      // Filtered empty state on customer3 (?status=paid)
      await cust3Page.goto('/orders?status=paid', { waitUntil: 'networkidle' });
      await expect(cust3Page.locator('[data-testid="orders-empty-state"]')).toContainText(
        'Không có đơn hàng nào khớp bộ lọc hiện tại'
      );

      expect(cust1Session.pageErrors).toEqual([]);
      expect(cust3Session.pageErrors).toEqual([]);
    } finally {
      await cust1Session.context.close();
      await cust3Session.context.close();
    }
  });

  test('13.3 Atmospheric Sign-In (/account/login) & Identity Profile (/account/profile) — returnUrl, validation, dev demo picker & logout', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const session = await createRoleSession(browser, state.baseUrl);

    try {
      const page = session.page;
      await page.setViewportSize({ width: 1440, height: 900 });

      // 1. Unauthenticated /account/login?returnUrl=%2Forders
      await page.goto('/account/login?returnUrl=%2Forders', { waitUntil: 'networkidle' });
      await assertZeroBootstrapAndNoBootstrapBlue(page);
      await expect(page.locator('[data-testid="login-page"]')).toBeVisible();
      await expect(page.locator('[data-testid="login-return-url-note"]')).toBeVisible();
      await expect(page.locator('[data-testid="dev-demo-account-picker"]')).toBeVisible();

      // Verify zero password leaks in login page HTML
      assertNoSecretLeakInText(await page.content(), state, 'Login Page HTML');

      // 2. Empty submit triggers client-side Zero-jQuery .field-validation-error in exact rgb(248, 113, 113)
      await page.click('[data-testid="login-submit-btn"]');
      const validationErrors = page.locator('.field-validation-error');
      await expect(validationErrors.first()).toBeVisible();
      const errColor = await validationErrors
        .first()
        .evaluate((el) => window.getComputedStyle(el).color);
      expect(errColor).toBe('rgb(248, 113, 113)');

      // 3. Clicking Dev Demo Quick-Picker chip populates email & focuses password
      await page.click('[data-testid="demo-chip-customer1"]');
      await expect(page.locator('input[name="Request.Email"]')).toHaveValue('customer1@demo.invalid');
      const activeName = await page.evaluate(() => (document.activeElement as HTMLInputElement)?.name);
      expect(activeName).toBe('Request.Password');

      // 4. Wrong password shows clear server authentication error alert while preserving returnUrl
      await page.fill('input[name="Request.Password"]', 'WrongPassword_123456!');
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('[data-testid="login-submit-btn"]'),
      ]);
      await expect(page.locator('[data-testid="login-error-alert"]')).toBeVisible();
      await expect(page.locator('[data-testid="login-error-alert"]')).toContainText(
        'Email hoặc mật khẩu không chính xác'
      );
      await expect(page.locator('input[name="Request.ReturnUrl"]')).toHaveValue('/orders');

      // 5. Valid password redirects directly to returnUrl (/orders)
      await page.fill('input[name="Request.Password"]', state.demoPassword);
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('[data-testid="login-submit-btn"]'),
      ]);
      await expect(page).toHaveURL(/\/orders$/);

      // 6. Visit /account/profile -> Zero-Bootstrap, role chip, UTC+7 creation time, paid stats, and POST logout
      await page.goto('/account/profile', { waitUntil: 'networkidle' });
      await assertZeroBootstrapAndNoBootstrapBlue(page);
      await expect(page.locator('[data-testid="profile-page"]')).toBeVisible();
      await expect(page.locator('body')).toContainText('customer1@demo.invalid');
      await expect(page.locator('body')).toContainText('UTC+7');
      await expect(page.locator('[data-testid="profile-role-chip"]')).toContainText('CUSTOMER');
      await expect(page.locator('body')).toContainText('đơn PAID');

      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('[data-testid="profile-logout-btn"]'),
      ]);
      await expect(page).toHaveURL(/\/(home)?$/);

      // Visiting /account/profile after logout must redirect back to /account/login
      await page.goto('/account/profile', { waitUntil: 'domcontentloaded' });
      await expect(page).toHaveURL(/\/account\/login/);

      expect(session.pageErrors).toEqual([]);
    } finally {
      await session.context.close();
    }
  });

  test('13.4 Responsive zero horizontal overflow across 320px, 390px, 768px, and 1440px on all UI-P6 routes', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const anonSession = await createRoleSession(browser, state.baseUrl);
    const custSession = await createRoleSession(browser, state.baseUrl);

    try {
      const anonPage = anonSession.page;
      const custPage = custSession.page;

      // Check /account/login across all viewports
      for (const width of [320, 390, 768, 1440]) {
        await anonPage.setViewportSize({ width, height: 844 });
        await anonPage.goto('/account/login?returnUrl=%2Forders', { waitUntil: 'networkidle' });
        const overflow = await anonPage.evaluate(
          () => document.documentElement.scrollWidth > window.innerWidth
        );
        expect(overflow, `/account/login overflow at ${width}px`).toBe(false);
      }

      // Authenticate customer1 and locate a PAID order ID for /orders/{id}/tickets
      await custPage.setViewportSize({ width: 1440, height: 900 });
      await loginInBrowser(custPage, 'customer1@demo.invalid', state.demoPassword);
      await custPage.goto('/orders?status=paid', { waitUntil: 'networkidle' });
      const firstPaidCard = custPage.locator('[data-testid="order-list-card"][data-order-status="PAID"]').first();
      let paidOrderId = Number((await firstPaidCard.getAttribute('data-order-id')) || '0');

      if (!paidOrderId) {
        await custPage.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
        await custPage.locator('.seat-box[data-bookable="true"]').first().click();
        await Promise.all([
          custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          custPage.click('#btnSubmit'),
        ]);
        paidOrderId = Number(custPage.url().match(/\/orders\/(\d+)$/)![1]);
        await custPage.goto(`/orders/${paidOrderId}/checkout`, { waitUntil: 'networkidle' });
        await Promise.all([
          custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          custPage.click('#btnPaySuccess'),
        ]);
      }

      const authRoutes = [`/orders/${paidOrderId}/tickets`, '/orders', '/account/profile'];
      for (const route of authRoutes) {
        for (const width of [320, 390, 768, 1440]) {
          await custPage.setViewportSize({ width, height: 844 });
          await custPage.goto(route, { waitUntil: 'networkidle' });
          const overflow = await custPage.evaluate(
            () => document.documentElement.scrollWidth > window.innerWidth
          );
          expect(overflow, `${route} overflow at ${width}px`).toBe(false);

          if (route.endsWith('/tickets')) {
            const qrRect = await custPage
              .locator('img[src*="/tickets/"][src*="/qr"]')
              .first()
              .boundingBox();
            expect(qrRect).not.toBeNull();
            expect(qrRect!.width, `QR width at ${width}px`).toBeGreaterThanOrEqual(120);
            expect(qrRect!.height, `QR height at ${width}px`).toBeGreaterThanOrEqual(120);
          }
        }
      }
    } finally {
      await anonSession.context.close();
      await custSession.context.close();
    }
  });
});
