import { test, expect, Page } from '@playwright/test';
import { loadE2eState, runRunnerCommandOrThrow } from '../e2e-env';
import { createRoleSession, formatVnDateTimeLocal, loginInBrowser } from './helpers';

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

  expect(audit.bodyBg).toBe('rgb(20, 20, 20)');
  expect(audit.hasBootstrapCss).toBe(false);
  expect(audit.hasDarkCompatCss).toBe(false);
  expect(audit.hasBootstrapJs).toBe(false);
  expect(audit.hasJQuery).toBe(false);
  expect(audit.hasWindowJQuery).toBe(false);
  expect(audit.hasBootstrapBlue).toBe(false);
}

test.describe('UI-P5: Transparent Seat Hold & Academic Mock Checkout (/orders/{id} & /orders/{id}/checkout)', () => {
  let showtimeId = 0;
  let setupAttempt = 0;

  test.beforeAll(async ({ browser }) => {
    const state = loadE2eState();
    runRunnerCommandOrThrow(['seed-verified-showcase'], state);

    const mgrSession = await createRoleSession(browser, state.baseUrl);
    try {
      const mgrPage = mgrSession.page;
      mgrPage.on('dialog', (d) => d.accept());
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);

      const moviesResp = await mgrPage.request.get('/api/movies');
      const movies = (await moviesResp.json()) as any[];
      const charade = movies.find((m) => m.provenance?.wikidataQid === 'Q496266') ?? movies[0];
      expect(charade).toBeTruthy();

      await mgrPage.goto('/management/showtimes/create');
      await mgrPage.selectOption('select[name="MovieId"]', String(charade.movieId));

      const phong02Opt = mgrPage.locator('select[name="RoomId"] option', { hasText: 'Phòng 02' }).first();
      const targetRoomOpt =
        (await phong02Opt.count()) > 0
          ? phong02Opt
          : mgrPage.locator('select[name="RoomId"] option:not([value=""])').first();
      const roomId = (await targetRoomOpt.getAttribute('value'))!;
      await mgrPage.selectOption('select[name="RoomId"]', roomId);

      const randomMinutes = Math.floor(Math.random() * 2400);
      const startTime = new Date(Date.now() + 9 * 24 * 60 * 60 * 1000 + randomMinutes * 60 * 1000);
      await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(startTime));
      await mgrPage.fill('input[name="BufferMinutes"]', '15');
      await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
      await mgrPage.fill('input[name="Prices[VIP]"]', '95000');

      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      showtimeId = Number(mgrPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);
      expect(showtimeId).toBeGreaterThan(0);
    } finally {
      await mgrSession.context.close();
    }
  });

  test('12.1 Zero-Bootstrap isolation, verified movie dossier, UTC+7 consistency, QR pass preview, honest MIS simulator, and PAID flow', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const custSession = await createRoleSession(browser, state.baseUrl);
    try {
      const page = custSession.page;
      await page.setViewportSize({ width: 1440, height: 900 });
      await loginInBrowser(page, 'customer1@demo.invalid', state.demoPassword);

      // Reserve A3 (Standard: 75k) + C5, C6 (VIP: 2x95k) = 265,000 VND
      await page.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await page.locator('.seat-box[data-seat-code="A3"]').click();
      await page.locator('.seat-box[data-seat-code="C5"]').click();
      await page.locator('.seat-box[data-seat-code="C6"]').click();
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('#btnSubmit'),
      ]);

      await expect(page).toHaveURL(/\/orders\/(\d+)$/);
      const orderId = Number(page.url().match(/\/orders\/(\d+)$/)![1]);

      // 1. Verify /orders/{id} Zero-Bootstrap & verified dossier
      await assertZeroBootstrapAndNoBootstrapBlue(page);
      await expect(page.locator('[data-testid="order-details-page"]')).toBeVisible();
      await expect(page.locator('[data-testid="order-hold-banner"]')).toContainText('Chờ thanh toán');
      await expect(page.locator('[data-testid="order-hold-banner"]')).toContainText(/Hết hạn giữ chỗ lúc\s+\d{2}:\d{2}/);

      const timer = page.locator('#countdownTimer');
      await expect(timer).toBeVisible();
      const remainingSec = Number(await timer.getAttribute('data-remaining'));
      expect(remainingSec).toBeGreaterThan(100);
      await expect(timer).toHaveText(/^\d{2}:\d{2}$/);

      // Verified poster & IMDb rating
      const orderPoster = page.locator('[data-testid="order-verified-film-card"] img.bgp-tx-film__poster');
      await expect(orderPoster).toBeVisible();
      expect(await orderPoster.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBeGreaterThan(0);
      expect(await orderPoster.getAttribute('srcset')).toContain('342w');
      await expect(page.locator('[data-testid="order-movie-rating"]')).toContainText('IMDb 7.8/10');
      await expect(page.locator('[data-testid="order-total-vnd"]')).toHaveText(/265[.,]000\s*đ/);

      // Ensure no raw unlocalized "... UTC" string appears in main content
      const mainText = await page.locator('main').innerText();
      expect(mainText).toMatch(/\d{2}\/\d{2}\/\d{4}/);
      expect(mainText).not.toMatch(/\d{2}:\d{2}:\d{2}\s+UTC\b/);

      // 2. Proceed to /orders/{id}/checkout
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click(`a[href="/orders/${orderId}/checkout"]`),
      ]);
      await page.waitForLoadState('networkidle');

      await assertZeroBootstrapAndNoBootstrapBlue(page);
      await expect(page.locator('[data-testid="checkout-page"]')).toBeVisible();
      const checkoutPoster = page.locator('[data-testid="checkout-verified-film-card"] img.bgp-tx-film__poster');
      await expect(checkoutPoster).toBeVisible();
      expect(await checkoutPoster.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBeGreaterThan(0);
      await expect(page.locator('[data-testid="checkout-movie-rating"]')).toContainText('IMDb 7.8/10');

      // 3. Verify perforated post-payment QR boarding pass preview with pure #FFFFFF quiet zone
      const qrPreview = page.locator('[data-testid="qr-pass-preview"]');
      await expect(qrPreview).toBeVisible();
      await expect(qrPreview).toContainText('Mẫu vé điện tử cấp nguyên tử sau khi PAID');
      await expect(qrPreview).toContainText('MINH HỌA VÉ QR');
      const qrBoxStyle = await page.locator('.bgp-qr-schematic-box').evaluate((el) => {
        const st = window.getComputedStyle(el);
        return { bg: st.backgroundColor, filter: st.filter };
      });
      expect(qrBoxStyle.bg).toBe('rgb(255, 255, 255)');
      expect(qrBoxStyle.filter).toBe('none');

      // 4. Verify sandbox payment gateway simulator (ZERO fake credit card / CVV / OTP inputs)
      const simPanel = page.locator('[data-testid="mis-payment-simulator"]');
      await expect(simPanel).toBeVisible();
      await expect(page.locator('[data-testid="mis-honest-disclosure"]')).toContainText('HMAC-SHA256');
      await expect(page.locator('[data-testid="mis-honest-disclosure"]')).toContainText('không thu tiền thật');

      const fakeCardInputsCount = await page.evaluate(() => {
        const cardSelectors =
          'input[name*="card" i], input[name*="cvv" i], input[name*="cvc" i], input[name*="otp" i], input[name*="expiry" i], input[autocomplete^="cc-"]';
        const visibleSimInputs = Array.from(
          document.querySelectorAll('[data-testid="mis-payment-simulator"] input:not([type="hidden"])')
        );
        return document.querySelectorAll(cardSelectors).length + visibleSimInputs.length;
      });
      expect(fakeCardInputsCount).toBe(0);

      // 5. Complete simulated SUCCESS payment and verify PAID state on /orders/{id}
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('#btnPaySuccess'),
      ]);
      await expect(page).toHaveURL(new RegExp(`/orders/${orderId}/tickets$`));

      await page.goto(`/orders/${orderId}`, { waitUntil: 'networkidle' });
      await expect(page.locator('[data-testid="order-state-card-paid"]')).toBeVisible();
      await expect(page.locator('[data-testid="order-hold-banner"]')).toContainText('Đã thanh toán');
      await expect(page.locator('[data-testid="order-hold-banner"]')).toContainText(/\d{2}\/\d{2}\/\d{4}/);
      await expect(page.locator(`a[href="/orders/${orderId}/tickets"]`).first()).toBeVisible();
    } finally {
      await custSession.context.close();
    }
  });

  test('12.2 Accessible <dialog> confirmations replace window.confirm() for CANCELLED and FAILED order flows', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const custSession = await createRoleSession(browser, state.baseUrl);
    try {
      const page = custSession.page;
      const nativeDialogs: string[] = [];
      page.on('dialog', async (d) => {
        nativeDialogs.push(`${d.type()}:${d.message()}`);
        await d.dismiss();
      });

      await page.setViewportSize({ width: 1440, height: 900 });
      await loginInBrowser(page, 'customer1@demo.invalid', state.demoPassword);

      // Reserve B3 + B4 -> Test <dialog> dismiss, Escape, and FAILED confirmation
      await page.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await page.locator('.seat-box[data-seat-code="B3"]').click();
      await page.locator('.seat-box[data-seat-code="B4"]').click();
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('#btnSubmit'),
      ]);
      const failOrderId = Number(page.url().match(/\/orders\/(\d+)$/)![1]);

      // Open cancel <dialog> on /orders/{id}, dismiss via button, then via Escape
      const cancelDialogOnDetails = page.locator('[data-testid="cancel-order-dialog"]');
      await page.click('#btnCancel');
      await expect(cancelDialogOnDetails).toBeVisible();
      await page.click('[data-testid="dismiss-cancel-dialog-btn"]');
      await expect(cancelDialogOnDetails).toBeHidden();
      await expect(page).toHaveURL(new RegExp(`/orders/${failOrderId}$`));

      await page.click('#btnCancel');
      await expect(cancelDialogOnDetails).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(cancelDialogOnDetails).toBeHidden();

      // Navigate to Checkout, test both dialogs, then confirm FAILED payment
      await page.goto(`/orders/${failOrderId}/checkout`, { waitUntil: 'networkidle' });
      await page.click('#btnCancelCheckout');
      await expect(page.locator('[data-testid="cancel-order-dialog"]')).toBeVisible();
      await page.click('[data-testid="dismiss-cancel-dialog-btn"]');
      await expect(page.locator('[data-testid="cancel-order-dialog"]')).toBeHidden();

      const failDialog = page.locator('[data-testid="fail-payment-dialog"]');
      await page.click('#btnPayFailed');
      await expect(failDialog).toBeVisible();
      await page.click('[data-testid="dismiss-fail-payment-btn"]');
      await expect(failDialog).toBeHidden();

      await page.click('#btnPayFailed');
      await expect(failDialog).toBeVisible();
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('[data-testid="confirm-fail-payment-btn"]'),
      ]);

      await expect(page).toHaveURL(new RegExp(`/orders/${failOrderId}$`));
      await expect(page.locator('[data-testid="order-state-card-failed"]')).toBeVisible();
      await expect(page.locator('[data-testid="order-hold-banner"]')).toContainText('FAILED');

      // Reserve B3 again (proving FAILED order released B3 immediately) and confirm CANCELLED via <dialog>
      await page.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await page.locator('.seat-box[data-seat-code="B3"]').click();
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('#btnSubmit'),
      ]);
      const cancelOrderId = Number(page.url().match(/\/orders\/(\d+)$/)![1]);

      await page.click('#btnCancel');
      await expect(page.locator('[data-testid="cancel-order-dialog"]')).toBeVisible();
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('[data-testid="confirm-cancel-order-btn"]'),
      ]);

      await expect(page.locator('[data-testid="order-state-card-cancelled"]')).toBeVisible();
      await expect(page.locator('[data-testid="order-hold-banner"]')).toContainText('Đã hủy');

      // ZERO native window.confirm() or window.alert() calls throughout!
      expect(nativeDialogs).toEqual([]);
    } finally {
      await custSession.context.close();
    }
  });

  test('12.3 Monotonic countdown reaching 00:00 auto-locks payment buttons & reveals reselect panel; EXPIRED & NEEDS_REVIEW states render clearly', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const custSession = await createRoleSession(browser, state.baseUrl);
    let orderId = 0;
    let orderCleanedUp = false;

    try {
      const page = custSession.page;
      await page.setViewportSize({ width: 1440, height: 900 });
      await loginInBrowser(page, 'customer1@demo.invalid', state.demoPassword);

      await page.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await page.locator('.seat-box[data-seat-code="C3"]').click();
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('#btnSubmit'),
      ]);
      orderId = Number(page.url().match(/\/orders\/(\d+)$/)![1]);

      // Set server expiry to 3 seconds ahead so we observe live transition to 00:00 (Hết hạn)
      runRunnerCommandOrThrow(['e2e-sql', 'set-order-remaining-seconds'], state, {
        BGP_E2E_ORDER_ID: String(orderId),
        BGP_E2E_REMAINING_SECONDS: '3',
      });

      await page.goto(`/orders/${orderId}/checkout`, { waitUntil: 'networkidle' });
      const inlineExpiredPanel = page.locator('[data-testid="hold-expired-inline-panel"]');
      await expect(inlineExpiredPanel).toBeVisible({ timeout: 8000 });

      await expect(page.locator('#countdownTimer')).toHaveText('00:00 (Hết hạn)');
      await expect(page.locator('#btnPaySuccess')).toBeDisabled();
      await expect(page.locator('#btnPayFailed')).toBeDisabled();
      await expect(page.locator('#btnCancelCheckout')).toBeDisabled();
      await expect(page.locator('[data-testid="checkout-reselect-seats-cta"]')).toHaveAttribute(
        'href',
        `/showtimes/${showtimeId}/seats`
      );

      // Run server-side expire sweep to release C3 and persist EXPIRED status
      runRunnerCommandOrThrow(['expire'], state);
      orderCleanedUp = true;

      await page.goto(`/orders/${orderId}`, { waitUntil: 'networkidle' });
      await expect(page.locator('[data-testid="order-state-card-expired"]')).toBeVisible();
      await expect(page.locator('[data-testid="order-hold-banner"]')).toContainText(/EXPIRED|Hết hạn/);
      await expect(page.locator('[data-testid="reselect-seats-cta"]')).toHaveAttribute(
        'href',
        `/showtimes/${showtimeId}/seats`
      );

      // Verify NEEDS_REVIEW operational reconciliation state
      runRunnerCommandOrThrow(['e2e-sql', 'set-order-needs-review'], state, {
        BGP_E2E_ORDER_ID: String(orderId),
      });
      await page.goto(`/orders/${orderId}`, { waitUntil: 'networkidle' });
      await expect(page.locator('[data-testid="order-state-card-review"]')).toBeVisible();
      await expect(page.locator('[data-testid="order-hold-banner"]')).toContainText('NEEDS_REVIEW');
      await expect(page.locator('[data-testid="order-state-card-review"]')).toContainText('đối soát');
    } finally {
      if (orderId > 0 && !orderCleanedUp) {
        runRunnerCommandOrThrow(['e2e-sql', 'force-expire-order'], state, {
          BGP_E2E_ORDER_ID: String(orderId),
        });
        runRunnerCommandOrThrow(['expire'], state);
      }
      await custSession.context.close();
    }
  });

  test('12.4 Strict 0px horizontal overflow across 320px, 390px, 768px, and 1440px on /orders/{id}, /orders/{id}/checkout, and open <dialog>', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const custSession = await createRoleSession(browser, state.baseUrl);
    let orderId = 0;

    try {
      const page = custSession.page;
      await loginInBrowser(page, 'customer1@demo.invalid', state.demoPassword);

      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await page.locator('.seat-box[data-seat-code="D3"]').click();
      await page.locator('.seat-box[data-seat-code="D4"]').click();
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('#btnSubmit'),
      ]);
      orderId = Number(page.url().match(/\/orders\/(\d+)$/)![1]);

      const viewports = [
        { width: 320, height: 568 },
        { width: 390, height: 844 },
        { width: 768, height: 1024 },
        { width: 1440, height: 900 },
      ];

      for (const vp of viewports) {
        await page.setViewportSize(vp);

        await page.goto(`/orders/${orderId}`, { waitUntil: 'networkidle' });
        const detailsOverflow = await page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth
        );
        expect(detailsOverflow, `Overflow on /orders/${orderId} at ${vp.width}px`).toBeLessThanOrEqual(0);

        await page.goto(`/orders/${orderId}/checkout`, { waitUntil: 'networkidle' });
        const checkoutOverflow = await page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth
        );
        expect(checkoutOverflow, `Overflow on /orders/${orderId}/checkout at ${vp.width}px`).toBeLessThanOrEqual(0);

        if (vp.width === 320) {
          await page.click('#btnPayFailed');
          await expect(page.locator('[data-testid="fail-payment-dialog"]')).toBeVisible();
          const dialogMetrics = await page.evaluate(() => {
            const dlg = document.querySelector('[data-testid="fail-payment-dialog"]') as HTMLElement;
            const rect = dlg.getBoundingClientRect();
            return {
              docOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
              left: rect.left,
              right: rect.right,
              viewportWidth: window.innerWidth,
            };
          });
          expect(dialogMetrics.docOverflow).toBeLessThanOrEqual(0);
          expect(dialogMetrics.left).toBeGreaterThanOrEqual(0);
          expect(dialogMetrics.right).toBeLessThanOrEqual(dialogMetrics.viewportWidth + 1);
          await page.click('[data-testid="dismiss-fail-payment-btn"]');
        }
      }

      // Cleanly cancel temporary hold via <dialog> so zero HELD seats remain
      await page.click('#btnCancelCheckout');
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        page.click('[data-testid="confirm-cancel-order-btn"]'),
      ]);
      orderId = 0;
    } finally {
      if (orderId > 0) {
        runRunnerCommandOrThrow(['e2e-sql', 'force-expire-order'], state, {
          BGP_E2E_ORDER_ID: String(orderId),
        });
        runRunnerCommandOrThrow(['expire'], state);
      }
      await custSession.context.close();
    }
  });
});
