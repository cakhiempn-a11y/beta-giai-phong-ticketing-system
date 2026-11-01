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
});
