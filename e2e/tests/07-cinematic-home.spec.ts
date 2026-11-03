import { test, expect } from '@playwright/test';
import { loadE2eState, runRunnerCommandOrThrow, runRunnerCleanupSafe, E2eState } from '../e2e-env';

let state: E2eState;
let baselineMoviesSnapshot = '';

test.describe('UI-P3: Netflix-Cinema Homepage (/), On-Sale Rail, Hover Quick-Preview & Trailer Modal', () => {
  test.beforeAll(() => {
    state = loadE2eState();
    baselineMoviesSnapshot = runRunnerCommandOrThrow(['e2e-sql', 'inspect-movies-state'], state).stdout.trim();
    // Seed fixtures to ensure multiple movies, broken poster target, draft movie, and zero-showtime movie exist
    runRunnerCommandOrThrow(['e2e-sql', 'seed-ui-fixtures'], state);
  });

  test.afterAll(() => {
    if (state) {
      runRunnerCommandOrThrow(['e2e-sql', 'reset-ui-fixtures'], state);
      const postResetSnapshot = runRunnerCommandOrThrow(['e2e-sql', 'inspect-movies-state'], state).stdout.trim();
      expect(postResetSnapshot).toBe(baselineMoviesSnapshot);
    }
  });

  test('7.1 Hero Billboard: renders full-bleed high-priority photographic backdrop, grounded badges, and accurate CTAs', async ({
    page,
  }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const hero = page.locator('.bgp-home-hero');
    await expect(hero).toBeVisible();

    const heroTitle = page.locator('.bgp-hero-title');
    await expect(heroTitle).toBeVisible();
    const titleText = await heroTitle.innerText();
    expect(titleText.trim().length).toBeGreaterThan(0);

    const eyebrow = hero.locator('.bgp-hero-eyebrow');
    await expect(eyebrow).toBeVisible();

    const primaryCta = hero.locator('[data-testid="hero-primary-cta"]');
    await expect(primaryCta).toBeVisible();
    const primaryHref = await primaryCta.getAttribute('href');
    expect(primaryHref).toMatch(/^\/movies\/\d+/);

    const primaryText = await primaryCta.innerText();
    const eyebrowText = await eyebrow.innerText();

    if (eyebrowText.includes('ĐANG CHIẾU')) {
      expect(primaryText).toContain('Xem lịch chiếu');
    } else {
      expect(primaryText).toContain('Chi tiết phim');
    }

    const secondaryCta = hero.locator('[data-testid="hero-secondary-cta"]');
    await expect(secondaryCta).toBeVisible();
    expect(await secondaryCta.innerText()).toContain('Khám phá phim');
    expect(await secondaryCta.getAttribute('href')).toBe('#now-showing');

    // Full-bleed verified backdrop with fetchpriority="high" and eager loading
    const backdropImg = hero.locator('[data-testid="hero-backdrop-img"]');
    await expect(backdropImg).toBeVisible();
    expect(await backdropImg.getAttribute('loading')).toBe('eager');
    expect(await backdropImg.getAttribute('fetchpriority')).toBe('high');
    expect(await backdropImg.getAttribute('src')).toMatch(/^\/media\/movies\/verified\//);
  });

  test('7.2 Movie Rail: horizontal scroll rail, 2:3 single-link cards, accessible buttons, and overflow scroll interactions', async ({
    page,
  }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const railSection = page.locator('#now-showing');
    await expect(railSection).toBeVisible();

    const rail = page.locator('[data-testid="movie-rail"]');
    await expect(rail).toBeVisible();

    const prevBtn = page.locator('[data-testid="rail-btn-prev"]');
    const nextBtn = page.locator('[data-testid="rail-btn-next"]');
    await expect(prevBtn).toBeVisible();
    await expect(nextBtn).toBeVisible();
    expect(await prevBtn.getAttribute('aria-label')).toBeTruthy();
    expect(await nextBtn.getAttribute('aria-label')).toBeTruthy();

    await expect(prevBtn).toBeDisabled();
    await expect(nextBtn).toBeEnabled();

    const cards = page.locator('[data-testid="movie-card"]');
    const cardCount = await cards.count();
    expect(cardCount).toBeGreaterThanOrEqual(4);

    const firstCard = cards.first();
    expect(await firstCard.evaluate((el) => el.tagName.toLowerCase())).toBe('a');
    const cardHref = await firstCard.getAttribute('href');
    expect(cardHref).toMatch(/^\/movies\/\d+/);

    // Card MUST NOT contain nested button or nested anchor tags (W3C / HTML5 validity)
    const nestedInteractive = await firstCard.evaluate((el) => ({
      nestedButtonsCount: el.querySelectorAll('button').length,
      nestedAnchorsCount: el.querySelectorAll('a').length,
    }));
    expect(nestedInteractive.nestedButtonsCount).toBe(0);
    expect(nestedInteractive.nestedAnchorsCount).toBe(0);

    const cardPoster = firstCard.locator('img');
    await expect(cardPoster).toBeVisible();
    expect(await cardPoster.getAttribute('loading')).toBe('lazy');

    const initialScroll = await rail.evaluate((el) => el.scrollLeft);
    expect(initialScroll).toBeLessThanOrEqual(5);

    await nextBtn.click();
    await page.waitForTimeout(600);

    const scrolledLeft = await rail.evaluate((el) => el.scrollLeft);
    expect(scrolledLeft).toBeGreaterThan(initialScroll);
    await expect(prevBtn).toBeEnabled();

    await prevBtn.click();
    await page.waitForTimeout(600);

    const returnScroll = await rail.evaluate((el) => el.scrollLeft);
    expect(returnScroll).toBeLessThan(scrolledLeft);

    await firstCard.focus();
    const isFocused = await firstCard.evaluate((el) => document.activeElement === el);
    expect(isFocused).toBe(true);
  });

  test('7.3 Artwork Fallback: real browser error interception transitions to vector fallback without infinite loops', async ({
    page,
  }) => {
    await page.route('**/posters/broken-poster-target.jpg', (route) => {
      route.fulfill({
        status: 404,
        contentType: 'image/jpeg',
        body: 'Not Found',
      });
    });

    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const targetMovieCard = page.locator('[data-testid="movie-card"]', { hasText: 'Phim Khong Co Suat' });
    await expect(targetMovieCard).toBeVisible();

    const targetImg = targetMovieCard.locator('img');
    await expect(targetImg).toBeVisible();

    await expect(targetImg).toHaveAttribute('src', '/images/poster-fallback.svg', { timeout: 5000 });

    const imgMetrics = await targetImg.evaluate((img: HTMLImageElement) => ({
      complete: img.complete,
      naturalWidth: img.naturalWidth,
      naturalHeight: img.naturalHeight,
      src: img.src,
    }));
    expect(imgMetrics.complete).toBe(true);
    expect(imgMetrics.naturalWidth).toBeGreaterThan(0);
    expect(imgMetrics.naturalHeight).toBeGreaterThan(0);

    const isolatedContext = await page.context().browser()!.newContext();
    const isolatedPage = await isolatedContext.newPage();
    await isolatedPage.route('**/images/poster-fallback.svg', (route) => route.abort());
    await isolatedPage.route('**/posters/broken-poster-target.jpg', (route) => route.abort());

    let errorCount = 0;
    isolatedPage.on('requestfailed', () => {
      errorCount++;
    });

    await isolatedPage.goto('/');
    await isolatedPage.waitForTimeout(1000);

    expect(errorCount).toBeLessThan(50);
    await isolatedContext.close();
  });

  test('7.4 Empty State & Snapshot Restoration: gracefully handles zero movies and preserves pre-existing DRAFT movies on restore', async ({
    page,
  }) => {
    const stateBeforeHide = runRunnerCommandOrThrow(['e2e-sql', 'inspect-movies-state'], state).stdout.trim();
    expect(stateBeforeHide).toContain('|DRAFT|Phim Ban Nhap Chua Cong Bo|');

    runRunnerCommandOrThrow(['e2e-sql', 'hide-all-movies'], state);

    let bodyError: unknown = null;
    try {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const emptyState = page.locator('.bgp-empty-state');
      await expect(emptyState).toBeVisible();
      await expect(emptyState).toContainText('Hiện chưa có phim nào đang chiếu');

      const heroTitle = page.locator('.bgp-hero-title');
      await expect(heroTitle).toContainText('Điện Ảnh Đích Thực');

      const heroDesc = page.locator('.bgp-hero-synopsis');
      await expect(heroDesc).toContainText('Chào mừng bạn đến với hệ thống rạp Beta Giải Phóng');

      const heroCta = page.locator('[data-testid="hero-primary-cta"]');
      await expect(heroCta).toBeVisible();
      expect(await heroCta.getAttribute('href')).toBe('/movies');
    } catch (err) {
      bodyError = err;
      throw err;
    } finally {
      runRunnerCleanupSafe(['e2e-sql', 'restore-all-movies'], state, bodyError);
    }

    const stateAfterRestore = runRunnerCommandOrThrow(['e2e-sql', 'inspect-movies-state'], state).stdout.trim();
    expect(stateAfterRestore).toBe(stateBeforeHide);
    expect(stateAfterRestore).toContain('|DRAFT|Phim Ban Nhap Chua Cong Bo|');

    await page.goto('/');
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="movie-card"]').first()).toBeVisible();
    await expect(page.locator('body')).not.toContainText('Phim Ban Nhap Chua Cong Bo');
  });

  test('7.5 Upcoming Showtimes: renders dark surface card with responsive table and valid seat map links', async ({
    page,
  }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const showtimesCard = page.locator('.bgp-upcoming-card');
    await expect(showtimesCard).toBeVisible();

    const tableHeaders = await showtimesCard.locator('thead th').allInnerTexts();
    expect(tableHeaders.some((h) => h.toLowerCase().includes('phim'))).toBe(true);
    expect(tableHeaders.some((h) => h.toLowerCase().includes('phòng'))).toBe(true);
    expect(tableHeaders.some((h) => h.toLowerCase().includes('giờ'))).toBe(true);
    expect(tableHeaders.some((h) => h.toLowerCase().includes('ghế'))).toBe(true);

    const bookingBtns = showtimesCard.locator('[data-testid="upcoming-seat-cta"]');
    expect(await bookingBtns.count()).toBeGreaterThan(0);
    const firstHref = await bookingBtns.first().getAttribute('href');
    expect(firstHref).toMatch(/^\/showtimes\/\d+\/seats/);
  });

  test('7.6 Responsive Viewports: strict assertion of no global horizontal overflow across 320px to 1440px', async ({
    browser,
  }) => {
    const viewports = [
      { width: 320, height: 600, label: '320px Narrow Mobile' },
      { width: 390, height: 844, label: '390x844 iPhone 12/13/14' },
      { width: 768, height: 1024, label: '768x1024 iPad Portrait' },
      { width: 992, height: 768, label: '992x768 Desktop Breakpoint' },
      { width: 1440, height: 900, label: '1440x900 Desktop Widescreen' },
    ];

    for (const vp of viewports) {
      const ctx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: vp.width, height: vp.height },
      });
      const page = await ctx.newPage();
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const overflow = await page.evaluate(() => {
        const docScrollWidth = document.documentElement.scrollWidth;
        const docClientWidth = document.documentElement.clientWidth;
        const bodyScrollWidth = document.body.scrollWidth;
        const bodyClientWidth = document.body.clientWidth;
        return {
          docScrollWidth,
          docClientWidth,
          bodyScrollWidth,
          bodyClientWidth,
          docDiff: docScrollWidth - docClientWidth,
          bodyDiff: bodyScrollWidth - bodyClientWidth,
        };
      });

      expect(
        overflow.docDiff,
        `Unexpected document horizontal overflow at ${vp.label} (scrollWidth=${overflow.docScrollWidth}, clientWidth=${overflow.docClientWidth})`
      ).toBeLessThanOrEqual(1);

      expect(
        overflow.bodyDiff,
        `Unexpected body horizontal overflow at ${vp.label} (scrollWidth=${overflow.bodyScrollWidth}, clientWidth=${overflow.bodyClientWidth})`
      ).toBeLessThanOrEqual(1);

      if (vp.width <= 768) {
        const prevBtn = page.locator('[data-testid="rail-btn-prev"]');
        if ((await prevBtn.count()) > 0) {
          expect(await prevBtn.isVisible()).toBe(false);
        }
      }

      await ctx.close();
    }
  });

  test('7.7 Navigation Integrity: browsing homepage and rails does not trigger mutation requests', async ({ page }) => {
    const mutationRequests: string[] = [];

    page.on('request', (req) => {
      const method = req.method().toUpperCase();
      const url = req.url();
      if ((method === 'POST' || method === 'PUT' || method === 'DELETE') && !url.includes('/antiforgery')) {
        mutationRequests.push(`${method} ${url}`);
      }
    });

    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const secondaryCta = page.locator('[data-testid="hero-secondary-cta"]');
    if (await secondaryCta.isVisible()) {
      await secondaryCta.click();
      await page.waitForTimeout(200);
    }

    const nextBtn = page.locator('[data-testid="rail-btn-next"]');
    if ((await nextBtn.isVisible()) && (await nextBtn.isEnabled())) {
      await nextBtn.click();
      await page.waitForTimeout(300);
    }

    expect(mutationRequests).toEqual([]);
  });

  test('7.8 Zero-Bootstrap Isolation, Grounded Ratings & Zero Fake Labels on /', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    const assetCheck = await page.evaluate(() => {
      const links = Array.from(document.querySelectorAll('link[rel="stylesheet"]')).map(
        (l) => l.getAttribute('href') || ''
      );
      const scripts = Array.from(document.querySelectorAll('script[src]')).map(
        (s) => s.getAttribute('src') || ''
      );
      return {
        hasBootstrapCss: links.some((h) => h.includes('bootstrap') || h.includes('dark-compat')),
        hasBootstrapJs: scripts.some((s) => s.includes('bootstrap') || s.includes('jquery')),
        bodyBg: window.getComputedStyle(document.body).backgroundColor,
        bodyText: document.body.innerText,
      };
    });

    expect(assetCheck.hasBootstrapCss).toBe(false);
    expect(assetCheck.hasBootstrapJs).toBe(false);
    expect(assetCheck.bodyBg).toBe('rgb(10, 10, 12)');
    expect(assetCheck.bodyText).not.toContain('98% phù hợp');
    expect(assetCheck.bodyText).not.toContain('Top 10');

    const ratingBadge = page.locator('[data-testid="hero-rating-badge"]');
    await expect(ratingBadge).toBeVisible();
    const ratingText = await ratingBadge.innerText();
    expect(ratingText).toMatch(/IMDb\s+\d+(\.\d+)?\/10\s+\([\d.,]+\s+lượt\)/);
  });

  test('7.9 On-Sale 16:9 Rail, Hover Quick-Preview Viewport Clamping & Touch Device Guard', async ({ browser }) => {
    const desktopCtx = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await desktopCtx.newPage();

    try {
      await page.goto('/', { waitUntil: 'networkidle' });

      // 16:9 On-Sale Rail with direct UTC+7 showtime chips
      const wideCards = page.locator('[data-testid="wide-movie-card"]');
      expect(await wideCards.count()).toBeGreaterThanOrEqual(3);
      const firstWideChip = wideCards.first().locator('[data-testid="wide-card-showtime-chip"]').first();
      await expect(firstWideChip).toBeVisible();
      expect(await firstWideChip.getAttribute('href')).toMatch(/^\/showtimes\/\d+\/seats$/);

      // Hover leftmost card in #now-showing -> popover opens after 320ms and clamps >= 8px from left edge
      const railCards = page.locator('[data-testid="movie-card"]');
      await railCards.first().scrollIntoViewIfNeeded();
      await railCards.first().hover();

      const popover = page.locator('[data-testid="quick-preview-popover"]');
      await expect(popover).toBeVisible({ timeout: 2500 });

      const leftBox = await popover.boundingBox();
      expect(leftBox).not.toBeNull();
      expect(leftBox!.x).toBeGreaterThanOrEqual(8);
      expect(leftBox!.x + leftBox!.width).toBeLessThanOrEqual(1440 - 8);
      expect(leftBox!.y).toBeGreaterThanOrEqual(8);
      expect(leftBox!.y + leftBox!.height).toBeLessThanOrEqual(900 - 8);

      // Escape dismisses popover cleanly
      await page.keyboard.press('Escape');
      await expect(popover).toBeHidden();

      // Explicit preview trigger on rightmost visible wide card -> clamps cleanly inside right viewport edge
      const previewTriggers = page.locator('[data-testid="wide-card-preview-btn"]');
      const visibleTriggerCount = await previewTriggers.count();
      const targetRightTrigger = previewTriggers.nth(Math.min(visibleTriggerCount - 1, 3));
      await targetRightTrigger.scrollIntoViewIfNeeded();
      await targetRightTrigger.click();

      await expect(popover).toBeVisible();
      const rightBox = await popover.boundingBox();
      expect(rightBox).not.toBeNull();
      expect(rightBox!.x).toBeGreaterThanOrEqual(8);
      expect(rightBox!.x + rightBox!.width).toBeLessThanOrEqual(1440 - 8);
    } finally {
      await desktopCtx.close();
    }

    // Touch device context: tapping card navigates directly without hover-popover trapping
    const touchCtx = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 390, height: 844 },
      hasTouch: true,
      isMobile: true,
    });
    const touchPage = await touchCtx.newPage();
    try {
      await touchPage.goto('/', { waitUntil: 'networkidle' });
      const firstCard = touchPage.locator('[data-testid="movie-card"]').first();
      const targetHref = (await firstCard.getAttribute('href'))!;
      await firstCard.tap();
      await expect(touchPage).toHaveURL(new RegExp(`${targetHref}$`));
    } finally {
      await touchCtx.close();
    }
  });

  test('7.10 Official YouTube Trailer Modal (<dialog>): click-to-load youtube-nocookie.com & clean iframe teardown on close', async ({
    page,
  }) => {
    await page.route('https://www.youtube-nocookie.com/embed/**', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'text/html; charset=utf-8',
        body: '<!doctype html><html><body style="background:#000;color:#fff;font-family:sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0">Official Trailer Embed Stub</body></html>',
      });
    });

    await page.goto('/', { waitUntil: 'networkidle' });

    const dialog = page.locator('[data-testid="trailer-dialog"]');
    await expect(dialog).toBeHidden();
    await expect(dialog.locator('iframe')).toHaveCount(0);

    const trailerBtn = page.locator('[data-testid="hero-trailer-btn"]');
    await expect(trailerBtn).toBeVisible();
    await trailerBtn.click();

    await expect(dialog).toBeVisible();
    const iframe = dialog.locator('[data-testid="trailer-iframe"]');
    await expect(iframe).toHaveCount(1);
    const iframeSrc = (await iframe.getAttribute('src')) || '';
    expect(iframeSrc).toContain('https://www.youtube-nocookie.com/embed/');

    // Close via Escape -> dialog closes and iframe is completely removed from DOM
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(dialog.locator('iframe')).toHaveCount(0);
  });
});
