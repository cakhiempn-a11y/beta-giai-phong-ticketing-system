import { test, expect } from '@playwright/test';
import { loadE2eState } from '../e2e-env';
import { createRoleSession, loginInBrowser } from './helpers';

const VIEWPORTS = [
  { name: 'Desktop', width: 1440, height: 900 },
  { name: 'Tablet', width: 768, height: 1024 },
  { name: 'Mobile', width: 390, height: 844 },
];

test.describe('BE-05B Phase 8: Responsive Viewports & Accessibility E2E Checks', () => {
  test('8.1 & 8.2: Desktop (1440x900), Tablet (768x1024), Mobile (390x844) layout, labels, keyboard navigation & QR visibility', async ({ browser }) => {
    const state = loadE2eState();
    const baseURL = state.baseUrl;

    const customerSession = await createRoleSession(browser, baseURL);
    const staffSession = await createRoleSession(browser, baseURL);
    const managerSession = await createRoleSession(browser, baseURL);

    try {
      const custPage = customerSession.page;
      const staffPage = staffSession.page;
      const mgrPage = managerSession.page;

      custPage.on('dialog', (d) => d.accept());
      mgrPage.on('dialog', (d) => d.accept());

      // 8.2 Accessibility check on Login page before logging in:
      await custPage.goto('/account/login');
      await expect(custPage.locator('label[for="Request_Email"]')).toBeVisible();
      await expect(custPage.locator('label[for="Request_Password"]')).toBeVisible();

      // Keyboard navigation check on Login form
      await custPage.locator('input[name="Request.Email"]').focus();
      await custPage.keyboard.type('customer1@demo.invalid');
      await custPage.keyboard.press('Tab');
      await custPage.keyboard.type(state.demoPassword);
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.keyboard.press('Enter'),
      ]);
      expect(custPage.url()).not.toContain('/account/login');

      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);

      // Find an active movie, showtime, and create or find a PAID order for Customer 1 so we can inspect all 9 pages across viewports
      await custPage.goto('/movies');
      const movieHref = (await custPage.locator('a[href*="/movies/"]').first().getAttribute('href'))!;
      await custPage.goto(movieHref);
      const seatMapHref = (await custPage.locator('a[href*="/showtimes/"][href*="/seats"]').first().getAttribute('href'))!;

      // Create a pending reservation for checkout inspection, and a paid order for ticket QR inspection
      await custPage.goto(seatMapHref);
      await custPage.locator('.seat-box[data-bookable="true"]').first().click();
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnSubmit'),
      ]);
      const paidOrderId = Number(custPage.url().match(/\/orders\/(\d+)$/)![1]);
      await custPage.goto(`/orders/${paidOrderId}/checkout`);
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnPaySuccess'),
      ]);

      // Create a pending order to inspect /orders/{id} and /orders/{id}/checkout
      await custPage.goto(seatMapHref);
      await custPage.locator('.seat-box[data-bookable="true"]').first().click();
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnSubmit'),
      ]);
      const pendingOrderId = Number(custPage.url().match(/\/orders\/(\d+)$/)![1]);

      for (const vp of VIEWPORTS) {
        // 1. Customer pages: /movies, /movies/{id}, /showtimes/{id}/seats, /orders/{id}, /orders/{id}/checkout, /orders/{id}/tickets
        await custPage.setViewportSize({ width: vp.width, height: vp.height });

        for (const route of [
          '/movies',
          movieHref,
          seatMapHref,
          `/orders/${pendingOrderId}`,
          `/orders/${pendingOrderId}/checkout`,
          `/orders/${paidOrderId}/tickets`,
        ]) {
          await custPage.goto(route);
          await expect(custPage.locator('main')).toBeVisible();

          // Verify main content container is within viewport width (long monospace order codes wrap or scroll safely)
          const mainWidth = await custPage.locator('main').evaluate((el) => el.getBoundingClientRect().width);
          expect(mainWidth, `Main container width on ${route} at ${vp.name}`).toBeLessThanOrEqual(vp.width + 60);
        }

        // Verify QR image on Mobile (390x844) and other viewports has width >= 120px and alt attribute
        await custPage.goto(`/orders/${paidOrderId}/tickets`);
        const qrImg = custPage.locator('img[src*="/tickets/"][src*="/qr"]').first();
        await expect(qrImg).toBeVisible();
        const altText = await qrImg.getAttribute('alt');
        expect(altText?.length).toBeGreaterThan(0);
        const box = await qrImg.boundingBox();
        expect(box).not.toBeNull();
        expect(box!.width).toBeGreaterThanOrEqual(120);
        expect(box!.height).toBeGreaterThanOrEqual(120);

        // 2. Staff page: /staff/check-in
        await staffPage.setViewportSize({ width: vp.width, height: vp.height });
        await staffPage.goto('/staff/check-in');
        await expect(staffPage.locator('#showtimeSelect')).toBeVisible();
        await expect(staffPage.locator('#btnStartCamera')).toBeVisible();
        await expect(staffPage.locator('label[for="qrFileInput"]')).toBeVisible();

        // 3. Manager pages: /management/movies, /management/showtimes, /management/reports
        await mgrPage.setViewportSize({ width: vp.width, height: vp.height });
        for (const mgrRoute of ['/management/movies', '/management/showtimes', '/management/reports']) {
          await mgrPage.goto(mgrRoute);
          await expect(mgrPage.locator('main')).toBeVisible();
        }
      }

      // Clean up pendingOrderId so no seat remains held unnecessarily
      await custPage.goto(`/orders/${pendingOrderId}`);
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click(`form[action="/orders/${pendingOrderId}/cancel"] button[type="submit"]`),
      ]);
    } finally {
      await customerSession.context.close();
      await staffSession.context.close();
      await managerSession.context.close();
    }
  });

  test('8.3: UI-01A Dark theme contrast invariants: bright badges, outline buttons, and input boundaries', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await context.newPage();

    // Helper: Calculate relative luminance and WCAG contrast ratio directly in test
    function parseRgb(colorStr: string): [number, number, number] {
      const match = colorStr.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
      if (!match) return [0, 0, 0];
      return [parseInt(match[1], 10), parseInt(match[2], 10), parseInt(match[3], 10)];
    }

    function getRelativeLuminance(rgb: [number, number, number]): number {
      const [r, g, b] = rgb.map((c) => {
        const s = c / 255;
        return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
      });
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    function calculateContrast(fgStr: string, bgStr: string): number {
      const l1 = getRelativeLuminance(parseRgb(fgStr));
      const l2 = getRelativeLuminance(parseRgb(bgStr));
      const lighter = Math.max(l1, l2);
      const darker = Math.min(l1, l2);
      return (lighter + 0.05) / (darker + 0.05);
    }

    try {
      // 1. Bright badge contrast on /movies (warning & info badges)
      await page.goto('/movies');
      await page.waitForLoadState('networkidle');

      const moviesBadges = await page.evaluate(() => {
        const warning = document.querySelector('.badge.bg-warning') as HTMLElement;
        const info = document.querySelector('.badge.bg-info') as HTMLElement;
        return {
          hasWarning: !!warning,
          warningColor: warning ? window.getComputedStyle(warning).color : '',
          warningBg: warning ? window.getComputedStyle(warning).backgroundColor : '',
          hasInfo: !!info,
          infoColor: info ? window.getComputedStyle(info).color : '',
          infoBg: info ? window.getComputedStyle(info).backgroundColor : '',
        };
      });

      // Assert mandatory elements exist (no silent skipping)
      expect(moviesBadges.hasWarning, 'Expected .badge.bg-warning on /movies').toBe(true);
      expect(moviesBadges.hasInfo, 'Expected .badge.bg-info on /movies').toBe(true);

      const warningRatio = calculateContrast(moviesBadges.warningColor, moviesBadges.warningBg);
      const infoRatio = calculateContrast(moviesBadges.infoColor, moviesBadges.infoBg);
      expect(warningRatio, `Warning badge contrast ratio (${warningRatio.toFixed(2)}:1)`).toBeGreaterThanOrEqual(7.0);
      expect(infoRatio, `Info badge contrast ratio (${infoRatio.toFixed(2)}:1)`).toBeGreaterThanOrEqual(7.0);

      // 2. Light badge inside card & actual outline button on /
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const homeElements = await page.evaluate(() => {
        function resolveEffectiveBg(el: HTMLElement): string {
          let curr: HTMLElement | null = el;
          while (curr) {
            const bg = window.getComputedStyle(curr).backgroundColor;
            if (bg && bg !== 'transparent' && bg !== 'rgba(0, 0, 0, 0)') {
              return bg;
            }
            curr = curr.parentElement;
          }
          return 'rgb(20, 20, 20)';
        }

        const lightBadgeInCard = document.querySelector('.card .badge.bg-light, .badge.bg-light') as HTMLElement;
        const outlineBtn = document.querySelector('.btn-outline-primary, .btn-outline-light') as HTMLElement;

        return {
          hasLightBadge: !!lightBadgeInCard,
          lightBadgeColor: lightBadgeInCard ? window.getComputedStyle(lightBadgeInCard).color : '',
          lightBadgeBg: lightBadgeInCard ? window.getComputedStyle(lightBadgeInCard).backgroundColor : '',
          hasOutlineBtn: !!outlineBtn,
          outlineBtnColor: outlineBtn ? window.getComputedStyle(outlineBtn).color : '',
          outlineBtnEffectiveBg: outlineBtn ? resolveEffectiveBg(outlineBtn) : '',
        };
      });

      expect(homeElements.hasLightBadge, 'Expected .badge.bg-light on /').toBe(true);
      expect(homeElements.hasOutlineBtn, 'Expected outline button on /').toBe(true);

      const lightBadgeRatio = calculateContrast(homeElements.lightBadgeColor, homeElements.lightBadgeBg);
      expect(lightBadgeRatio, `Light badge contrast ratio (${lightBadgeRatio.toFixed(2)}:1)`).toBeGreaterThanOrEqual(7.0);

      const outlineBtnRatio = calculateContrast(homeElements.outlineBtnColor, homeElements.outlineBtnEffectiveBg);
      expect(outlineBtnRatio, `Outline button text contrast ratio (${outlineBtnRatio.toFixed(2)}:1)`).toBeGreaterThanOrEqual(4.5);

      // 3. Form input boundaries and focus on /account/login
      await page.goto('/account/login');
      await page.waitForLoadState('networkidle');

      const inputStyles = await page.evaluate(() => {
        const input = document.querySelector('input[name="Request.Email"]') as HTMLInputElement;
        if (!input) return null;
        const style = window.getComputedStyle(input);
        return {
          borderColor: style.borderColor,
          backgroundColor: style.backgroundColor,
          color: style.color,
        };
      });

      expect(inputStyles).not.toBeNull();
      // Form control border (#767676) must achieve >= 3:1 non-text contrast against input bg
      const borderRatio = calculateContrast(inputStyles!.borderColor, inputStyles!.backgroundColor);
      expect(borderRatio, `Input border contrast ratio (${borderRatio.toFixed(2)}:1)`).toBeGreaterThanOrEqual(3.0);

      // Form control text must be readable light text
      const inputTextRatio = calculateContrast(inputStyles!.color, inputStyles!.backgroundColor);
      expect(inputTextRatio, `Input text contrast ratio (${inputTextRatio.toFixed(2)}:1)`).toBeGreaterThanOrEqual(7.0);

      // 4. Validation typography vs general text-danger
      await page.click('button[type="submit"]');
      await page.waitForTimeout(300);

      const valStyles = await page.evaluate(() => {
        const valMsg = document.querySelector('.field-validation-error, .invalid-feedback');
        return valMsg ? { fontSize: window.getComputedStyle(valMsg).fontSize, color: window.getComputedStyle(valMsg).color } : null;
      });

      expect(valStyles, 'Expected validation error after submitting empty form').not.toBeNull();
      expect(valStyles!.color).toBe('rgb(248, 113, 113)');
    } finally {
      await context.close();
    }
  });

  test('8.4: UI-01A Strict 320px viewport shell overflow assertion (scrollWidth <= clientWidth)', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 320, height: 600 },
    });
    const page = await context.newPage();

    try {
      const routesToCheck = ['/', '/movies', '/account/login', '/home/privacy'];
      for (const route of routesToCheck) {
        await page.goto(route);
        await page.waitForLoadState('networkidle');

        const overflow = await page.evaluate(() => {
          const docScroll = document.documentElement.scrollWidth;
          const docClient = document.documentElement.clientWidth;
          const bodyScroll = document.body.scrollWidth;
          const bodyClient = document.body.clientWidth;
          return {
            docScroll,
            docClient,
            bodyScroll,
            bodyClient,
            hasDocHorizontalScroll: docScroll > docClient,
            hasBodyHorizontalScroll: bodyScroll > bodyClient,
          };
        });

        expect(
          overflow.hasDocHorizontalScroll,
          `Route ${route} at 320px width has document horizontal scroll (scrollWidth=${overflow.docScroll}, clientWidth=${overflow.docClient})`
        ).toBe(false);
        expect(
          overflow.hasBodyHorizontalScroll,
          `Route ${route} at 320px width has body horizontal scroll (scrollWidth=${overflow.bodyScroll}, clientWidth=${overflow.bodyClient})`
        ).toBe(false);
      }
    } finally {
      await context.close();
    }
  });

  test('8.5: UI-01A Operational role navigation in mobile viewports', async ({ browser }) => {
    const state = loadE2eState();
    const staffContext = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 390, height: 844 },
    });
    const staffPage = await staffContext.newPage();

    const mgrContext = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 390, height: 844 },
    });
    const mgrPage = await mgrContext.newPage();

    try {
      // Staff on mobile
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);
      await staffPage.goto('/');
      await staffPage.waitForLoadState('networkidle');

      const staffToggler = staffPage.locator('.navbar-toggler');
      await expect(staffToggler).toBeVisible();
      await staffToggler.click();
      await staffPage.waitForTimeout(400);

      // Staff must see CheckIn links in mobile menu
      await expect(staffPage.locator('nav a[href*="/staff/check-in"]').first()).toBeVisible();

      // Manager on mobile
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);
      await mgrPage.goto('/');
      await mgrPage.waitForLoadState('networkidle');

      const mgrToggler = mgrPage.locator('.navbar-toggler');
      await expect(mgrToggler).toBeVisible();
      await mgrToggler.click();
      await mgrPage.waitForTimeout(400);

      // Manager must see Vận hành dropdown in mobile menu
      await expect(mgrPage.locator('nav #managementDropdown')).toBeVisible();
    } finally {
      await staffContext.close();
      await mgrContext.close();
    }
  });
});
