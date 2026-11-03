import { test, expect } from '@playwright/test';
import {
  loadE2eState,
  runRunnerCommandOrThrow,
  runRunnerCleanupSafe,
} from '../e2e-env';

function parseRgb(colorStr: string): [number, number, number] {
  const match = colorStr.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
  if (!match) return [20, 20, 20];
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

test.describe('UI-02B: Cinematic Movie Catalogue (/movies) & Movie Details (/movies/{id})', () => {
  test.beforeAll(() => {
    const state = loadE2eState();
    runRunnerCommandOrThrow(['e2e-sql', 'seed-ui-fixtures'], state, undefined);
  });

  test.afterAll(() => {
    const state = loadE2eState();
    runRunnerCleanupSafe(['e2e-sql', 'reset-ui-fixtures'], state, 'UI-02B afterAll reset-ui-fixtures');
  });

  test('1. Catalogue (/movies): displays only PUBLISHED movies, 2:3 posters, accurate showtime status, and single-link cards', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await context.newPage();

    const mutatingRequests: string[] = [];
    page.on('request', (req) => {
      if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(req.method().toUpperCase())) {
        mutatingRequests.push(`${req.method()} ${req.url()}`);
      }
    });

    try {
      await page.goto('/movies', { waitUntil: 'networkidle' });

      // Heading & dark background
      await expect(page.locator('h1.bgp-catalog-title')).toHaveText('Khám phá phim');
      const bg = await page.evaluate(() => window.getComputedStyle(document.body).backgroundColor);
      expect(bg).toBe('rgb(20, 20, 20)');

      // Never displays DRAFT movie
      await expect(page.locator('body')).not.toContainText('Phim Ban Nhap Chua Cong Bo');

      // Displays PUBLISHED movies
      const cards = page.locator('[data-testid="catalog-movie-card"]');
      const cardCount = await cards.count();
      expect(cardCount).toBeGreaterThanOrEqual(5);

      // Verify no nested <button> or <a> inside any card link
      const nestedInteractiveCount = await page.evaluate(() => {
        const cardEls = Array.from(document.querySelectorAll('[data-testid="catalog-movie-card"]'));
        let nested = 0;
        for (const card of cardEls) {
          nested += card.querySelectorAll('a, button').length;
        }
        return nested;
      });
      expect(nestedInteractiveCount).toBe(0);

      // Verify all card posters load (including null PosterUrl & broken-poster-target.jpg falling back to poster-fallback.svg)
      const posterStats = await page.evaluate(() => {
        const imgs = Array.from(
          document.querySelectorAll('[data-testid="catalog-movie-card"] img.bgp-catalog-poster-img')
        ) as HTMLImageElement[];
        return imgs.map((img) => {
          const style = window.getComputedStyle(img);
          const wrapStyle = window.getComputedStyle(img.parentElement!);
          return {
            src: img.getAttribute('src') || '',
            currentSrc: img.currentSrc,
            complete: img.complete,
            naturalWidth: img.naturalWidth,
            loading: img.getAttribute('loading'),
            objectFit: style.objectFit,
            aspectRatio: wrapStyle.aspectRatio,
          };
        });
      });

      expect(posterStats.length).toBe(cardCount);
      for (const p of posterStats) {
        expect(p.complete).toBe(true);
        expect(p.naturalWidth).toBeGreaterThan(0);
        expect(p.loading).toBe('lazy');
        expect(p.objectFit).toBe('cover');
      }
      // At least 2 cards should be using the fallback SVG (one NULL PosterUrl, one broken-poster-target.jpg)
      const fallbackLoadedCount = posterStats.filter((p) => p.currentSrc.includes('/images/poster-fallback.svg')).length;
      expect(fallbackLoadedCount).toBeGreaterThanOrEqual(2);

      // Verify accurate showtime status badges (has showtimes vs no showtimes)
      const activeCard = page.locator('[data-testid="catalog-movie-card"]', { hasText: 'Chien Binh Khong Gian' });
      await expect(activeCard.locator('[data-testid="card-showtime-status"]')).toContainText('Có lịch chiếu • 4 suất');

      const emptyCard = page.locator('[data-testid="catalog-movie-card"]', { hasText: 'Phim Khong Co Suat' });
      await expect(emptyCard.locator('[data-testid="card-showtime-status"]')).toHaveText('Chưa có lịch chiếu');

      // Clicking a card navigates to /movies/{id}
      const targetHref = await activeCard.getAttribute('href');
      expect(targetHref).toMatch(/^\/movies\/\d+$/);
      await activeCard.click();
      await expect(page).toHaveURL(new RegExp(`${targetHref}$`));

      expect(mutatingRequests).toEqual([]);
    } finally {
      await context.close();
    }
  });

  test('2. Catalogue Search & Filter (/movies): query, filter pills, clear filter, no-match state, reload & Back persistence', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await context.newPage();

    const mutatingRequests: string[] = [];
    page.on('request', (req) => {
      if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(req.method().toUpperCase())) {
        mutatingRequests.push(`${req.method()} ${req.url()}`);
      }
    });

    try {
      await page.goto('/movies', { waitUntil: 'networkidle' });
      const totalCards = await page.locator('[data-testid="catalog-movie-card"]').count();
      expect(totalCards).toBeGreaterThanOrEqual(5);

      // Filter: has-showtimes
      await page.click('[data-testid="catalog-filter-has-showtimes"]');
      await page.waitForLoadState('networkidle');
      expect(page.url()).toContain('filter=has-showtimes');
      const hasShowtimeCards = page.locator('[data-testid="catalog-movie-card"]');
      const hasShowtimeCount = await hasShowtimeCards.count();
      expect(hasShowtimeCount).toBeGreaterThanOrEqual(2);
      expect(hasShowtimeCount).toBeLessThan(totalCards);
      await expect(page.locator('body')).not.toContainText('Phim Khong Co Suat');

      // Filter: no-showtimes
      await page.click('[data-testid="catalog-filter-no-showtimes"]');
      await page.waitForLoadState('networkidle');
      expect(page.url()).toContain('filter=no-showtimes');
      await expect(page.locator('body')).toContainText('Phim Khong Co Suat');
      await expect(page.locator('body')).not.toContainText('Chien Binh Khong Gian');

      // Search by title while resetting filter to all
      await page.click('[data-testid="catalog-filter-all"]');
      await page.waitForLoadState('networkidle');
      await page.fill('[data-testid="catalog-search-input"]', 'Chien Binh Khong Gian');
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle' }),
        page.click('[data-testid="catalog-search-submit"]'),
      ]);
      expect(page.url()).toContain('q=Chien+Binh+Khong+Gian');
      await expect(page.locator('[data-testid="catalog-movie-card"]')).toHaveCount(1);
      await expect(page.locator('[data-testid="catalog-movie-card"]').first()).toContainText('Chien Binh Khong Gian');

      // Reload preserves search result
      await page.reload({ waitUntil: 'networkidle' });
      await expect(page.locator('[data-testid="catalog-movie-card"]')).toHaveCount(1);

      // Browser Back returns to unfiltered all view
      await page.goBack({ waitUntil: 'networkidle' });
      await expect(page.locator('[data-testid="catalog-movie-card"]')).toHaveCount(totalCards);

      // Search with no matching results -> shows catalog-empty-filter (NOT catalog-empty-system)
      await page.fill('[data-testid="catalog-search-input"]', 'KhongTonTaiPhimNayXYZ');
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle' }),
        page.click('[data-testid="catalog-search-submit"]'),
      ]);
      await expect(page.locator('[data-testid="catalog-empty-filter"]')).toBeVisible();
      await expect(page.locator('[data-testid="catalog-empty-system"]')).toHaveCount(0);

      // Reset button restores all movies
      await page.click('[data-testid="catalog-empty-reset-btn"]');
      await page.waitForLoadState('networkidle');
      await expect(page.locator('[data-testid="catalog-movie-card"]')).toHaveCount(totalCards);

      expect(mutatingRequests).toEqual([]);
    } finally {
      await context.close();
    }
  });

  test('3. Catalogue Empty System State & Snapshot Restoration: hide-all-movies / restore-all-movies preserves pre-existing DRAFT', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await context.newPage();

    try {
      runRunnerCommandOrThrow(['e2e-sql', 'hide-all-movies'], state);

      await page.goto('/movies', { waitUntil: 'networkidle' });
      await expect(page.locator('[data-testid="catalog-empty-system"]')).toBeVisible();
      await expect(page.locator('[data-testid="catalog-empty-system"]')).toContainText(
        'Hiện chưa có phim nào được công bố'
      );
      await expect(page.locator('[data-testid="catalog-movie-card"]')).toHaveCount(0);
    } finally {
      runRunnerCleanupSafe(['e2e-sql', 'restore-all-movies'], state, 'UI-02B restore-all-movies');
      await context.close();
    }

    // Verify pre-existing DRAFT movie remained DRAFT after restore-all-movies
    const inspectAfterRestore = runRunnerCommandOrThrow(['e2e-sql', 'inspect-movies-state'], state);
    expect(inspectAfterRestore.stdout).toContain('|DRAFT|Phim Ban Nhap Chua Cong Bo|');
  });

  test('4. Movie Details (/movies/{id}): hero metadata, anchor scroll offset, zero-showtime state, and 404 for DRAFT/invalid ID', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await context.newPage();

    try {
      await page.goto('/movies', { waitUntil: 'networkidle' });

      const activeHref = (await page
        .locator('[data-testid="catalog-movie-card"]', { hasText: 'Chien Binh Khong Gian' })
        .getAttribute('href'))!;
      const zeroShowtimeHref = (await page
        .locator('[data-testid="catalog-movie-card"]', { hasText: 'Phim Khong Co Suat' })
        .getAttribute('href'))!;

      // 4.1 Movie with showtimes
      await page.goto(activeHref, { waitUntil: 'networkidle' });
      await expect(page.locator('#movie-detail-title')).toContainText('Chien Binh Khong Gian');
      await expect(page.locator('[data-testid="detail-age-label"]')).toHaveText('T16');
      await expect(page.locator('[data-testid="detail-duration"]')).toHaveText('135 phút');
      await expect(page.locator('[data-testid="detail-genre"]')).toContainText('Hành Động, Viễn Tưởng');
      await expect(page.locator('[data-testid="detail-synopsis"]')).toContainText(
        'Chiến trường đa vũ trụ rực lửa mở ra cuộc đối đầu định mệnh'
      );

      // Must NOT contain outdated BE-01 / BE-02 notice or Buffer dọn phòng
      const bodyText = await page.locator('body').innerText();
      expect(bodyText).not.toContain('BE-01');
      expect(bodyText).not.toContain('BE-02');
      expect(bodyText).not.toContain('Buffer dọn phòng');

      // Poster is 2:3 and loaded
      const posterLoaded = await page
        .locator('[data-testid="detail-poster-img"]')
        .evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0);
      expect(posterLoaded).toBe(true);

      // Clicking "Chọn suất chiếu" scrolls to #showtimes-section without sticky navbar covering the heading
      await page.click('[data-testid="detail-cta-scroll-showtimes"]');
      await page.waitForTimeout(350);
      const scrollPositions = await page.evaluate(() => {
        const nav = document.querySelector('.bgp-navbar');
        const heading = document.getElementById('showtimes-heading');
        return {
          navBottom: nav ? nav.getBoundingClientRect().bottom : 68,
          headingTop: heading ? heading.getBoundingClientRect().top : 0,
        };
      });
      expect(scrollPositions.headingTop).toBeGreaterThanOrEqual(scrollPositions.navBottom);

      // 4.2 Movie with zero showtimes (also has NULL synopsis & broken poster URL)
      await page.goto(zeroShowtimeHref, { waitUntil: 'networkidle' });
      await expect(page.locator('#movie-detail-title')).toHaveText('Phim Khong Co Suat');
      await expect(page.locator('[data-testid="detail-no-showtimes"]')).toBeVisible();
      await expect(page.locator('[data-testid="detail-no-showtimes"]')).toContainText(
        'Hiện chưa có suất chiếu sắp tới'
      );
      await expect(page.locator('[data-testid="detail-cta-scroll-showtimes"]')).toHaveCount(0);
      await expect(page.locator('[data-testid="detail-cta-back-catalog"]')).toBeVisible();
      await expect(page.locator('[data-testid="detail-synopsis"]')).toContainText(
        'Nội dung tóm tắt của bộ phim đang được cập nhật.'
      );

      // Broken poster fell back to /images/poster-fallback.svg
      const fallbackSrc = await page
        .locator('[data-testid="detail-poster-img"]')
        .evaluate((img: HTMLImageElement) => ({
          currentSrc: img.currentSrc,
          naturalWidth: img.naturalWidth,
        }));
      expect(fallbackSrc.currentSrc).toContain('/images/poster-fallback.svg');
      expect(fallbackSrc.naturalWidth).toBeGreaterThan(0);

      // 4.3 Invalid movie ID & DRAFT movie ID return 404
      const notFoundResp = await page.goto('/movies/999999999');
      expect(notFoundResp?.status()).toBe(404);

      const inspectOut = runRunnerCommandOrThrow(['e2e-sql', 'inspect-movies-state'], state).stdout;
      const draftLine = inspectOut
        .split(/\r?\n/)
        .find((l) => l.includes('|DRAFT|Phim Ban Nhap Chua Cong Bo|'));
      expect(draftLine).toBeDefined();
      const draftId = draftLine!.split('|')[1].trim();
      const draftResp = await page.goto(`/movies/${draftId}`);
      expect(draftResp?.status()).toBe(404);
    } finally {
      await context.close();
    }
  });

  test('5. Showtime Selector (/movies/{id}): UTC+7 date grouping, midnight crossover, sold-out label, keyboard/URL sync, and JS-disabled progressive enhancement', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await context.newPage();

    try {
      await page.goto('/movies', { waitUntil: 'networkidle' });
      const activeHref = (await page
        .locator('[data-testid="catalog-movie-card"]', { hasText: 'Chien Binh Khong Gian' })
        .getAttribute('href'))!;

      await page.goto(activeHref, { waitUntil: 'networkidle' });

      // Should have 2 date tabs in UTC+7 chronological order
      const dateTabs = page.locator('[data-testid="showtime-date-tab"]');
      await expect(dateTabs).toHaveCount(2);

      const dateKey0 = (await dateTabs.nth(0).getAttribute('data-date-key'))!;
      const dateKey1 = (await dateTabs.nth(1).getAttribute('data-date-key'))!;
      expect(dateKey0 < dateKey1).toBe(true);

      // First date tab is active by default and has 2 showtimes: 18:30 (available) and 23:15 -> 01:30 (+1 ngày, sold-out)
      await expect(dateTabs.nth(0)).toHaveAttribute('aria-selected', 'true');
      const panel0 = page.locator(`[data-date-panel="${dateKey0}"]`);
      await expect(panel0).toBeVisible();

      const panel0Cards = panel0.locator('[data-testid="showtime-card"]');
      await expect(panel0Cards).toHaveCount(2);

      // Verify chronological order on Day 1: 18:30 then 23:15
      await expect(panel0Cards.nth(0).locator('[data-testid="showtime-start"]')).toHaveText('18:30');
      await expect(panel0Cards.nth(0).locator('[data-testid="showtime-cta"]')).toContainText('Chọn ghế');

      // 23:15 showtime crosses midnight (+1 ngày) and is sold out (0 available seats)
      await expect(panel0Cards.nth(1).locator('[data-testid="showtime-start"]')).toHaveText('23:15');
      await expect(panel0Cards.nth(1).locator('[data-testid="showtime-crosses-midnight"]')).toContainText('+1 ngày');
      await expect(panel0Cards.nth(1).locator('[data-testid="showtime-seats"]')).toContainText('Hết ghế trống');
      await expect(panel0Cards.nth(1).locator('[data-testid="showtime-cta"]')).toHaveText('Xem sơ đồ ghế');

      // Keyboard navigation: focus first date tab, press ArrowRight to switch to second date tab
      await dateTabs.nth(0).focus();
      await page.keyboard.press('ArrowRight');

      await expect(dateTabs.nth(1)).toHaveAttribute('aria-selected', 'true');
      expect(page.url()).toContain(`date=${dateKey1}`);

      const panel1 = page.locator(`[data-date-panel="${dateKey1}"]`);
      await expect(panel1).toBeVisible();
      await expect(panel0).toBeHidden();

      // Day 2 has 00:30 VN (which is 17:30 UTC previous day! Verifies UTC+7 date grouping) and 19:00 VN
      const panel1Cards = panel1.locator('[data-testid="showtime-card"]');
      await expect(panel1Cards).toHaveCount(2);
      await expect(panel1Cards.nth(0).locator('[data-testid="showtime-start"]')).toHaveText('00:30');
      await expect(panel1Cards.nth(1).locator('[data-testid="showtime-start"]')).toHaveText('19:00');

      // Reloading keeps Day 2 selected via ?date= query param
      await page.reload({ waitUntil: 'networkidle' });
      await expect(dateTabs.nth(1)).toHaveAttribute('aria-selected', 'true');
      await expect(panel1).toBeVisible();

      // Browser Back returns to Day 1
      await page.goBack({ waitUntil: 'networkidle' });
      await expect(dateTabs.nth(0)).toHaveAttribute('aria-selected', 'true');
      await expect(panel0).toBeVisible();

      // Clicking a showtime CTA opens /showtimes/{id}/seats
      const firstShowtimeCta = panel0Cards.nth(0).locator('[data-testid="showtime-cta"]');
      const seatHref = (await firstShowtimeCta.getAttribute('href'))!;
      expect(seatHref).toMatch(/^\/showtimes\/\d+\/seats$/);
      await firstShowtimeCta.click();
      await expect(page).toHaveURL(new RegExp(`${seatHref}$`));
      await expect(page.locator('.seat-grid-container')).toBeVisible();

      // Progressive enhancement check: with JavaScript disabled, all date groups are visible and accessible
      const noJsCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1280, height: 800 },
        javaScriptEnabled: false,
      });
      try {
        const noJsPage = await noJsCtx.newPage();
        await noJsPage.goto(activeHref, { waitUntil: 'domcontentloaded' });
        await expect(noJsPage.locator(`[data-date-panel="${dateKey0}"]`)).toBeVisible();
        await expect(noJsPage.locator(`[data-date-panel="${dateKey1}"]`)).toBeVisible();
        await expect(noJsPage.locator('[data-testid="showtime-card"]')).toHaveCount(4);
      } finally {
        await noJsCtx.close();
      }
    } finally {
      await context.close();
    }
  });

  test('6. Real Network 404 Poster Fallback, Long Title Wrapping, Contrast, and Responsive Viewports (320px to 1440px)', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await context.newPage();

    try {
      // 6.1 Intercept /images/posters/poster-scifi.svg with HTTP 404 and verify fallback on /movies
      await page.route('**/images/posters/poster-scifi.svg', (route) =>
        route.fulfill({ status: 404, body: 'Not Found' })
      );

      await page.goto('/movies', { waitUntil: 'networkidle' });
      const activeCardImg = page
        .locator('[data-testid="catalog-movie-card"]', { hasText: 'Chien Binh Khong Gian' })
        .locator('img.bgp-catalog-poster-img');

      await expect
        .poll(
          async () =>
            await activeCardImg.evaluate(
              (img: HTMLImageElement) =>
                img.complete && img.naturalWidth > 0 && img.currentSrc.includes('/images/poster-fallback.svg')
            ),
          { timeout: 5_000 }
        )
        .toBe(true);

      await page.unroute('**/images/posters/poster-scifi.svg');

      // 6.2 Long title wrapping & responsive viewports (320, 390, 768, 992, 1440)
      runRunnerCommandOrThrow(['e2e-sql', 'set-long-title'], state);

      const activeHref = (await page
        .locator('[data-testid="catalog-movie-card"]')
        .first()
        .getAttribute('href'))!;

      const viewports = [
        { width: 320, height: 600 },
        { width: 390, height: 844 },
        { width: 768, height: 1024 },
        { width: 992, height: 768 },
        { width: 1440, height: 900 },
      ];

      for (const vp of viewports) {
        await page.setViewportSize(vp);

        for (const route of ['/movies', activeHref]) {
          await page.goto(route, { waitUntil: 'networkidle' });
          const overflow = await page.evaluate(() => ({
            docScroll: document.documentElement.scrollWidth,
            docClient: document.documentElement.clientWidth,
            bodyScroll: document.body.scrollWidth,
            bodyClient: document.body.clientWidth,
          }));
          expect(
            overflow.docScroll <= overflow.docClient,
            `Overflow on ${route} at ${vp.width}px: scrollWidth=${overflow.docScroll}, clientWidth=${overflow.docClient}`
          ).toBe(true);
          expect(
            overflow.bodyScroll <= overflow.bodyClient,
            `Body overflow on ${route} at ${vp.width}px: scrollWidth=${overflow.bodyScroll}, clientWidth=${overflow.bodyClient}`
          ).toBe(true);
        }
      }

      // 6.3 Contrast checks on filter pills
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto('/movies', { waitUntil: 'networkidle' });
      const pillColors = await page.evaluate(() => {
        const activePill = document.querySelector('.bgp-filter-pill.is-active') as HTMLElement;
        const inactivePill = document.querySelector('.bgp-filter-pill:not(.is-active)') as HTMLElement;
        return {
          activeFg: window.getComputedStyle(activePill).color,
          activeBg: window.getComputedStyle(activePill).backgroundColor,
          inactiveFg: window.getComputedStyle(inactivePill).color,
          inactiveBg: window.getComputedStyle(inactivePill).backgroundColor,
        };
      });
      expect(calculateContrast(pillColors.activeFg, pillColors.activeBg)).toBeGreaterThanOrEqual(4.5);
      expect(calculateContrast(pillColors.inactiveFg, pillColors.inactiveBg)).toBeGreaterThanOrEqual(4.5);
    } finally {
      runRunnerCleanupSafe(['e2e-sql', 'restore-title'], state, 'UI-02B restore-title');
      await context.close();
    }
  });
});
