import { test, expect } from '@playwright/test';
import { loadE2eState, runRunnerCommand, E2eState } from '../e2e-env';

let state: E2eState;

test.describe('UI-02A: Cinematic Homepage & Movie Artwork Discovery E2E Suite', () => {
  test.beforeAll(() => {
    state = loadE2eState();
    // Seed fixtures to ensure multiple movies, broken poster target, and zero-showtime movie exist
    const res = runRunnerCommand(['e2e-sql', 'seed-ui-fixtures'], state);
    if (res.status !== 0) {
      console.warn(`[Fixtures] Failed to seed UI fixtures: ${res.stderr || res.stdout}`);
    }
  });

  test.afterAll(() => {
    if (state) {
      runRunnerCommand(['e2e-sql', 'reset-ui-fixtures'], state);
    }
  });

  test('7.1 Hero Section: renders large cinematic hero, metadata badges, dynamic copy accuracy, and eager poster', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    // Hero container
    const hero = page.locator('.bgp-home-hero');
    await expect(hero).toBeVisible();

    // Hero title
    const heroTitle = page.locator('.bgp-hero-title');
    await expect(heroTitle).toBeVisible();
    const titleText = await heroTitle.innerText();
    expect(titleText.trim().length).toBeGreaterThan(0);

    // Hero eyebrow & dynamic copy matching upcoming showtimes
    const eyebrow = hero.locator('.bgp-hero-eyebrow');
    await expect(eyebrow).toBeVisible();

    const primaryCta = hero.locator('.bgp-hero-actions a.btn-primary');
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

    // Secondary CTA: "Khám phá phim" smooth scrolling to #now-showing
    const secondaryCta = hero.locator('.bgp-hero-actions a.btn-outline-light');
    await expect(secondaryCta).toBeVisible();
    const secondaryText = await secondaryCta.innerText();
    expect(secondaryText).toContain('Khám phá phim');
    expect(await secondaryCta.getAttribute('href')).toBe('#now-showing');

    // Hero poster frame
    const posterFrame = hero.locator('.bgp-hero-poster-frame');
    await expect(posterFrame).toBeVisible();
    const posterImg = posterFrame.locator('img');
    await expect(posterImg).toBeVisible();
    expect(await posterImg.getAttribute('loading')).toBe('eager');
  });

  test('7.2 Movie Rail: horizontal scroll rail, 2:3 cards, accessible buttons, and overflow scroll interactions', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const railSection = page.locator('#now-showing');
    await expect(railSection).toBeVisible();

    const rail = page.locator('[data-testid="movie-rail"]');
    await expect(rail).toBeVisible();

    // Navigation buttons with unified selectors
    const prevBtn = page.locator('[data-testid="rail-btn-prev"]');
    const nextBtn = page.locator('[data-testid="rail-btn-next"]');
    await expect(prevBtn).toBeVisible();
    await expect(nextBtn).toBeVisible();
    expect(await prevBtn.getAttribute('aria-label')).toBeTruthy();
    expect(await nextBtn.getAttribute('aria-label')).toBeTruthy();

    // With ui fixtures seeded, rail has overflow
    // Initial state: Prev button is disabled at boundary (scrollLeft = 0)
    await expect(prevBtn).toBeDisabled();
    await expect(nextBtn).toBeEnabled();

    // Cards
    const cards = page.locator('[data-testid="movie-card"]');
    const cardCount = await cards.count();
    expect(cardCount).toBeGreaterThanOrEqual(4);

    const firstCard = cards.first();
    // Card must be an anchor tag pointing to /movies/{id}
    expect(await firstCard.evaluate((el) => el.tagName.toLowerCase())).toBe('a');
    const cardHref = await firstCard.getAttribute('href');
    expect(cardHref).toMatch(/^\/movies\/\d+/);

    // Card MUST NOT contain nested button or nested anchor tags (W3C / HTML5 validity)
    const nestedInteractive = await firstCard.evaluate((el) => {
      const nestedButtons = el.querySelectorAll('button');
      const nestedAnchors = el.querySelectorAll('a');
      return {
        nestedButtonsCount: nestedButtons.length,
        nestedAnchorsCount: nestedAnchors.length,
      };
    });
    expect(nestedInteractive.nestedButtonsCount).toBe(0);
    expect(nestedInteractive.nestedAnchorsCount).toBe(0);

    // Poster inside card must use lazy loading
    const cardPoster = firstCard.locator('img');
    await expect(cardPoster).toBeVisible();
    expect(await cardPoster.getAttribute('loading')).toBe('lazy');

    // Scroll interaction test: Next increases scrollLeft, Prev decreases scrollLeft
    const initialScroll = await rail.evaluate((el) => el.scrollLeft);
    expect(initialScroll).toBeLessThanOrEqual(5);

    // Click Next button
    await nextBtn.click();
    await page.waitForTimeout(600);

    const scrolledLeft = await rail.evaluate((el) => el.scrollLeft);
    expect(scrolledLeft).toBeGreaterThan(initialScroll);

    // Now Prev button should become enabled
    await expect(prevBtn).toBeEnabled();

    // Click Prev button to scroll back
    await prevBtn.click();
    await page.waitForTimeout(600);

    const returnScroll = await rail.evaluate((el) => el.scrollLeft);
    expect(returnScroll).toBeLessThan(scrolledLeft);

    // Keyboard focus: focusing card should be achievable
    await firstCard.focus();
    const isFocused = await firstCard.evaluate((el) => document.activeElement === el);
    expect(isFocused).toBe(true);
  });

  test('7.3 Artwork Fallback: real browser error interception transitions to vector fallback without infinite loops', async ({ page }) => {
    // Intercept target broken poster URL to return HTTP 404
    await page.route('**/posters/broken-poster-target.jpg', (route) => {
      route.fulfill({
        status: 404,
        contentType: 'image/jpeg',
        body: 'Not Found',
      });
    });

    await page.goto('/');
    await page.waitForLoadState('networkidle');

    // Find the movie card for "Phim Khong Co Suat" which has the broken poster target
    const targetMovieCard = page.locator('[data-testid="movie-card"]', { hasText: 'Phim Khong Co Suat' });
    await expect(targetMovieCard).toBeVisible();

    const targetImg = targetMovieCard.locator('img');
    await expect(targetImg).toBeVisible();

    // Verify natural browser transition to fallback without manual test intervention
    await expect(targetImg).toHaveAttribute('src', '/images/poster-fallback.svg', { timeout: 5000 });

    // Assert that the image is complete and has natural dimensions
    const imgMetrics = await targetImg.evaluate((img: HTMLImageElement) => ({
      complete: img.complete,
      naturalWidth: img.naturalWidth,
      naturalHeight: img.naturalHeight,
      src: img.src,
    }));
    expect(imgMetrics.complete).toBe(true);
    expect(imgMetrics.naturalWidth).toBeGreaterThan(0);
    expect(imgMetrics.naturalHeight).toBeGreaterThan(0);

    // Verify infinite loop prevention:
    // If fallback image ALSO fails (e.g. 404 or abort), ensure no infinite loop occurs
    const loopErrors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        loopErrors.push(msg.text());
      }
    });

    // Create a new isolated page where even the fallback URL returns 404
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

    // Image error events must be bounded and not infinite
    expect(errorCount).toBeLessThan(50);
    await isolatedContext.close();
  });

  test('7.4 Empty State: gracefully handles zero movies with general cinema greeting and restored state', async ({ page }) => {
    // 1. Hide all movies
    runRunnerCommand(['e2e-sql', 'hide-all-movies'], state);

    try {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      // Empty state visible in rail
      const emptyState = page.locator('.bgp-empty-state');
      await expect(emptyState).toBeVisible();
      await expect(emptyState).toContainText('Hiện chưa có phim nào đang chiếu');

      // Hero should show general cinema welcome copy
      const heroTitle = page.locator('.bgp-hero-title');
      await expect(heroTitle).toContainText('Điện Ảnh Đích Thực');

      const heroDesc = page.locator('.bgp-hero-synopsis');
      await expect(heroDesc).toContainText('Chào mừng bạn đến với hệ thống rạp Beta Giải Phóng');

      // Primary CTA in empty state links to /movies
      const heroCta = page.locator('.bgp-hero-actions a.btn-primary');
      await expect(heroCta).toBeVisible();
      expect(await heroCta.getAttribute('href')).toBe('/movies');
    } finally {
      // 2. Restore all movies
      runRunnerCommand(['e2e-sql', 'restore-all-movies'], state);
    }

    // Verify restoration
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    await expect(page.locator('[data-testid="movie-card"]').first()).toBeVisible();
  });

  test('7.5 Upcoming Showtimes: renders dark surface card with responsive table and valid seat map links', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const showtimesCard = page.locator('.bgp-upcoming-card');
    if (await showtimesCard.isVisible()) {
      // Headers verification
      const tableHeaders = await showtimesCard.locator('thead th').allInnerTexts();
      expect(tableHeaders.some((h) => h.toLowerCase().includes('phim'))).toBe(true);
      expect(tableHeaders.some((h) => h.toLowerCase().includes('phòng'))).toBe(true);
      expect(tableHeaders.some((h) => h.toLowerCase().includes('giờ'))).toBe(true);
      expect(tableHeaders.some((h) => h.toLowerCase().includes('ghế'))).toBe(true);

      // Check "Xem sơ đồ ghế" button links
      const bookingBtns = showtimesCard.locator('tbody a.btn');
      const btnCount = await bookingBtns.count();
      if (btnCount > 0) {
        const firstBtn = bookingBtns.first();
        const href = await firstBtn.getAttribute('href');
        expect(href).toMatch(/^\/showtimes\/\d+\/seats/);
      }
    }
  });

  test('7.6 Responsive Viewports: strict assertion of no global horizontal overflow across 320px to 1440px', async ({ browser }) => {
    const viewports = [
      { width: 320, height: 600, label: '320px Narrow Mobile' },
      { width: 390, height: 844, label: '390x844 iPhone 12/13/14' },
      { width: 768, height: 1024, label: '768x1024 iPad Portrait' },
      { width: 992, height: 768, label: '992x768 Desktop Breakpoint' },
      { width: 1440, height: 900, label: '1440x900 Desktop Widescreen' },
    ];

    for (const vp of viewports) {
      const ctx = await browser.newContext({ viewport: { width: vp.width, height: vp.height } });
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

      // On mobile viewports (<=768px), check that rail arrow buttons are hidden
      if (vp.width <= 768) {
        const prevBtn = page.locator('[data-testid="rail-btn-prev"]');
        if (await prevBtn.count() > 0) {
          const isVisible = await prevBtn.isVisible();
          expect(isVisible).toBe(false);
        }
      }

      await ctx.close();
    }
  });

  test('7.7 Navigation Integrity: browsing homepage and rail does not trigger mutation requests', async ({ page }) => {
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

    // Click secondary CTA "Khám phá phim" (smooth scroll)
    const secondaryCta = page.locator('.bgp-home-hero a.btn-outline-light');
    if (await secondaryCta.isVisible()) {
      await secondaryCta.click();
      await page.waitForTimeout(200);
    }

    // Click Next button on rail if visible and enabled
    const nextBtn = page.locator('[data-testid="rail-btn-next"]');
    if (await nextBtn.isVisible() && await nextBtn.isEnabled()) {
      await nextBtn.click();
      await page.waitForTimeout(300);
    }

    // No unexpected mutations should have occurred
    expect(mutationRequests).toEqual([]);
  });
});
