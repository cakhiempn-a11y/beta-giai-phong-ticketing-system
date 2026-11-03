import { test, expect } from '@playwright/test';
import { loadE2eState, runRunnerCommandOrThrow } from '../e2e-env';
import { createRoleSession, loginInBrowser } from './helpers';

test.describe('UI-P1: Netflix-Cinema Design System Foundation & Dual-Shell Navigation', () => {
  test.beforeAll(() => {
    const state = loadE2eState();
    runRunnerCommandOrThrow(['e2e-sql', 'seed-admin'], state);
  });

  test('9.1 Consumer Header: overlay-to-solid scroll transition and inline header movie search (/movies?q=...)', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await context.newPage();

    try {
      await page.goto('/', { waitUntil: 'networkidle' });

      const header = page.locator('[data-testid="cinema-header"]');
      await expect(header).toBeVisible();
      await expect(header).toHaveAttribute('data-header-mode', 'overlay');

      // At scrollY = 0 on Hero page, header is not in scrolled solid state
      await expect(header).not.toHaveClass(/bgp-cinema-header--scrolled/);

      // Scroll past 32px -> header transitions to solid frosted obsidian
      await page.evaluate(() => window.scrollTo({ top: 180, behavior: 'instant' as ScrollBehavior }));
      await expect(header).toHaveClass(/bgp-cinema-header--scrolled/);

      await expect
        .poll(async () => header.evaluate((el) => window.getComputedStyle(el).backgroundColor))
        .toContain('10, 10, 12');

      // Scroll back to top -> returns to translucent overlay mode
      await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' as ScrollBehavior }));
      await expect(header).not.toHaveClass(/bgp-cinema-header--scrolled/);

      // Inline header search navigates to /movies?q=... and preserves keyword in header & catalog inputs
      const headerInput = page.locator('[data-testid="header-search-input"]');
      await headerInput.fill('Hành động');
      await Promise.all([
        page.waitForURL(/\/movies\?q=/, { waitUntil: 'networkidle' }),
        page.click('[data-testid="header-search-trigger"]'),
      ]);

      expect(page.url()).toContain('/movies?q=');
      await expect(page.locator('[data-testid="header-search-input"]')).toHaveValue('Hành động');
      await expect(page.locator('[data-testid="catalog-search-input"]')).toHaveValue('Hành động');

      // Non-hero page (/movies) keeps header in solid mode even at scrollY = 0
      await expect(page.locator('[data-testid="cinema-header"]')).toHaveAttribute('data-header-mode', 'solid');
      await expect(page.locator('[data-testid="cinema-header"]')).toHaveClass(/bgp-cinema-header--solid/);
    } finally {
      await context.close();
    }
  });

  test('9.2 Dual-Shell Role Separation: Guest, CUSTOMER, STAFF, MANAGER & ADMIN navigation boundaries', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const baseURL = state.baseUrl;

    const guestSession = await createRoleSession(browser, baseURL);
    const custSession = await createRoleSession(browser, baseURL);
    const staffSession = await createRoleSession(browser, baseURL);
    const mgrSession = await createRoleSession(browser, baseURL);
    const adminSession = await createRoleSession(browser, baseURL);

    try {
      for (const s of [guestSession, custSession, staffSession, mgrSession, adminSession]) {
        await s.page.setViewportSize({ width: 1440, height: 900 });
      }

      // 1. Guest on /
      await guestSession.page.goto('/', { waitUntil: 'networkidle' });
      await expect(guestSession.page.locator('[data-testid="cinema-login-btn"]')).toBeVisible();
      await expect(guestSession.page.locator('[data-testid="ops-console-switch"]')).toHaveCount(0);
      await expect(guestSession.page.locator('[data-testid="cinema-desktop-nav"]')).not.toContainText('Đơn hàng của tôi');
      await expect(guestSession.page.locator('[data-testid="cinema-desktop-nav"]')).not.toContainText('Soát vé');
      await expect(guestSession.page.locator('[data-testid="cinema-desktop-nav"]')).not.toContainText('Vận hành');

      // 2. CUSTOMER on /
      await loginInBrowser(custSession.page, 'customer1@demo.invalid', state.demoPassword);
      await custSession.page.goto('/', { waitUntil: 'networkidle' });
      await expect(custSession.page.locator('[data-testid="cinema-desktop-nav"]')).toContainText('Đơn hàng của tôi');
      await expect(custSession.page.locator('[data-testid="ops-console-switch"]')).toHaveCount(0);
      await expect(custSession.page.locator('[data-testid="cinema-user-menu"]')).toBeVisible();

      // 3. STAFF: Consumer shell shows single switch -> /staff/check-in in _OperationsLayout (no Management nav)
      await loginInBrowser(staffSession.page, 'staff@demo.invalid', state.demoPassword);
      await staffSession.page.goto('/', { waitUntil: 'networkidle' });
      await expect(staffSession.page.locator('[data-testid="cinema-desktop-nav"]')).not.toContainText('Soát vé');
      const staffSwitch = staffSession.page.locator('[data-testid="ops-console-switch"]');
      await expect(staffSwitch).toHaveCount(1);
      await expect(staffSwitch).toHaveAttribute('href', '/staff/check-in');

      await staffSwitch.click();
      await expect(staffSession.page).toHaveURL(/\/staff\/check-in/);
      await expect(staffSession.page.locator('[data-testid="ops-shell"]')).toBeVisible();
      await expect(staffSession.page.locator('[data-testid="ops-role-chip"]')).toHaveText('STAFF');
      await expect(staffSession.page.locator('[data-testid="ops-sidebar"]')).toContainText('Soát vé QR');
      await expect(staffSession.page.locator('[data-testid="ops-management-nav"]')).toHaveCount(0);

      // 4. MANAGER: Consumer shell shows single switch -> /management/reports in _OperationsLayout
      await loginInBrowser(mgrSession.page, 'manager@demo.invalid', state.demoPassword);
      await mgrSession.page.goto('/', { waitUntil: 'networkidle' });
      await expect(mgrSession.page.locator('[data-testid="cinema-desktop-nav"]')).not.toContainText('Quản lý Phim');
      const mgrSwitch = mgrSession.page.locator('[data-testid="ops-console-switch"]');
      await expect(mgrSwitch).toHaveCount(1);
      await expect(mgrSwitch).toHaveAttribute('href', '/management/reports');

      await mgrSwitch.click();
      await expect(mgrSession.page).toHaveURL(/\/management\/reports/);
      await expect(mgrSession.page.locator('[data-testid="ops-shell"]')).toBeVisible();
      await expect(mgrSession.page.locator('[data-testid="ops-role-chip"]')).toHaveText('MANAGER');
      await expect(mgrSession.page.locator('[data-testid="ops-management-nav"]')).toContainText('Quản lý Phim');

      // Return link from Operations Console back to Consumer Cinema shell
      await mgrSession.page.click('[data-testid="ops-return-cinema"]');
      await expect(mgrSession.page).toHaveURL(/\/$/);
      await expect(mgrSession.page.locator('[data-testid="cinema-header"]')).toBeVisible();

      // 5. ADMIN: Enters Operations Console with ADMIN role badge
      await loginInBrowser(adminSession.page, 'admin@demo.invalid', state.demoPassword);
      await adminSession.page.goto('/management/movies', { waitUntil: 'networkidle' });
      await expect(adminSession.page.locator('[data-testid="ops-shell"]')).toBeVisible();
      await expect(adminSession.page.locator('[data-testid="ops-role-chip"]')).toHaveText('ADMIN');
    } finally {
      await guestSession.context.close();
      await custSession.context.close();
      await staffSession.context.close();
      await mgrSession.context.close();
      await adminSession.context.close();
    }
  });

  test('9.3 Pure Zero-Bootstrap Static Shell Pages (/privacy, /account/access-denied, /error) & Local WOFF2 Fonts', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await context.newPage();

    try {
      const upgradedRoutes = ['/privacy', '/account/access-denied', '/access-denied', '/error?statusCode=404'];
      for (const route of upgradedRoutes) {
        await page.goto(route, { waitUntil: 'networkidle' });

        const pageAudit = await page.evaluate(async () => {
          await document.fonts.ready;
          const stylesheets = Array.from(document.querySelectorAll('link[rel="stylesheet"]')).map(
            (l) => l.getAttribute('href') || ''
          );
          const scripts = Array.from(document.querySelectorAll('script[src]')).map(
            (s) => s.getAttribute('src') || ''
          );
          return {
            hasBootstrapCss: stylesheets.some((h) => h.includes('bootstrap')),
            hasDarkCompatCss: stylesheets.some((h) => h.includes('dark-compat')),
            hasJquery: scripts.some((s) => s.includes('jquery')),
            bodyBg: window.getComputedStyle(document.body).backgroundColor,
            fontFamily: window.getComputedStyle(document.body).fontFamily,
            beVietnamLoaded: document.fonts.check('700 18px "Be Vietnam Pro"'),
          };
        });

        expect(pageAudit.hasBootstrapCss, `Bootstrap CSS must not load on ${route}`).toBe(false);
        expect(pageAudit.hasDarkCompatCss, `dark-compat.css must not load on ${route}`).toBe(false);
        expect(pageAudit.hasJquery, `jQuery must not load on ${route}`).toBe(false);
        expect(pageAudit.bodyBg).toBe('rgb(10, 10, 12)');
        expect(pageAudit.fontFamily).toContain('Be Vietnam Pro');
        expect(pageAudit.beVietnamLoaded).toBe(true);
      }

      // Check provenance & academic notices on /privacy
      await page.goto('/privacy', { waitUntil: 'networkidle' });
      await expect(page.locator('h1')).toContainText('Giới Thiệu Đề Án');
      await expect(page.locator('body')).toContainText('This product uses the TMDB API');
      await expect(page.locator('body')).toContainText('IMDb Non-Commercial Datasets');
    } finally {
      await context.close();
    }
  });

  test('9.4 Responsive Zero-Overflow (320px, 390px, 768px, 1440px) & Keyboard :focus-visible Ring on Both Shells', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const viewports = [
      { width: 320, height: 600 },
      { width: 390, height: 844 },
      { width: 768, height: 1024 },
      { width: 1440, height: 900 },
    ];

    const session = await createRoleSession(browser, state.baseUrl);
    try {
      await loginInBrowser(session.page, 'manager@demo.invalid', state.demoPassword);

      const routes = ['/', '/privacy', '/account/access-denied', '/staff/check-in', '/management/movies'];
      for (const vp of viewports) {
        await session.page.setViewportSize(vp);
        for (const route of routes) {
          await session.page.goto(route, { waitUntil: 'networkidle' });
          const metrics = await session.page.evaluate(() => ({
            docScroll: document.documentElement.scrollWidth,
            docClient: document.documentElement.clientWidth,
            bodyScroll: document.body.scrollWidth,
            bodyClient: document.body.clientWidth,
          }));
          expect(
            metrics.docScroll <= metrics.docClient,
            `Doc horizontal overflow on ${route} at ${vp.width}px (${metrics.docScroll} > ${metrics.docClient})`
          ).toBe(true);
          expect(
            metrics.bodyScroll <= metrics.bodyClient,
            `Body horizontal overflow on ${route} at ${vp.width}px (${metrics.bodyScroll} > ${metrics.bodyClient})`
          ).toBe(true);
        }
      }

      // Keyboard :focus-visible ring check on /privacy
      await session.page.setViewportSize({ width: 1440, height: 900 });
      await session.page.goto('/privacy', { waitUntil: 'networkidle' });
      await session.page.keyboard.press('Tab'); // Focuses skip link
      const skipLinkBoxShadow = await session.page.evaluate(() => {
        const active = document.activeElement as HTMLElement | null;
        return active ? window.getComputedStyle(active).boxShadow : '';
      });
      expect(skipLinkBoxShadow).not.toBe('none');
      expect(skipLinkBoxShadow.length).toBeGreaterThan(5);
    } finally {
      await session.context.close();
    }
  });
});
