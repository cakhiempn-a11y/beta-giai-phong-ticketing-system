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

test.describe('UI-P3: Cinematic Movie Catalogue (/movies) & Movie Details (/movies/{id})', () => {
  test.beforeAll(() => {
    const state = loadE2eState();
    runRunnerCommandOrThrow(['e2e-sql', 'seed-ui-fixtures'], state, undefined);
  });

  test.afterAll(() => {
    const state = loadE2eState();
    runRunnerCleanupSafe(['e2e-sql', 'reset-ui-fixtures'], state, 'UI-P3 afterAll reset-ui-fixtures');
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
      await page.goto('/movies?pageSize=48', { waitUntil: 'networkidle' });

      // Heading & Zero-Bootstrap Netflix dark canvas (#141414 -> rgb(20, 20, 20))
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
      // At least 2 cards should be using the fallback SVG (PENDING_TMDB_API_KEY NULL PosterUrl + broken-poster-target.jpg)
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
      runRunnerCleanupSafe(['e2e-sql', 'restore-all-movies'], state, 'UI-P3 restore-all-movies');
      await context.close();
    }

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
      await page.goto('/movies?pageSize=48', { waitUntil: 'networkidle' });

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

      const bodyText = await page.locator('body').innerText();
      expect(bodyText).not.toContain('BE-01');
      expect(bodyText).not.toContain('BE-02');
      expect(bodyText).not.toContain('Buffer dọn phòng');

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
      await page.goto('/movies?pageSize=48', { waitUntil: 'networkidle' });
      const activeHref = (await page
        .locator('[data-testid="catalog-movie-card"]', { hasText: 'Chien Binh Khong Gian' })
        .getAttribute('href'))!;

      await page.goto(activeHref, { waitUntil: 'networkidle' });

      const dateTabs = page.locator('[data-testid="showtime-date-tab"]');
      await expect(dateTabs).toHaveCount(2);

      const dateKey0 = (await dateTabs.nth(0).getAttribute('data-date-key'))!;
      const dateKey1 = (await dateTabs.nth(1).getAttribute('data-date-key'))!;
      expect(dateKey0 < dateKey1).toBe(true);

      await expect(dateTabs.nth(0)).toHaveAttribute('aria-selected', 'true');
      const panel0 = page.locator(`[data-date-panel="${dateKey0}"]`);
      await expect(panel0).toBeVisible();

      const panel0Cards = panel0.locator('[data-testid="showtime-card"]');
      await expect(panel0Cards).toHaveCount(2);

      await expect(panel0Cards.nth(0).locator('[data-testid="showtime-start"]')).toHaveText('18:30');
      await expect(panel0Cards.nth(0).locator('[data-testid="showtime-cta"]')).toContainText('Chọn ghế');

      await expect(panel0Cards.nth(1).locator('[data-testid="showtime-start"]')).toHaveText('23:15');
      await expect(panel0Cards.nth(1).locator('[data-testid="showtime-crosses-midnight"]')).toContainText('+1 ngày');
      await expect(panel0Cards.nth(1).locator('[data-testid="showtime-seats"]')).toContainText('Hết ghế trống');
      await expect(panel0Cards.nth(1).locator('[data-testid="showtime-cta"]')).toHaveText('Xem sơ đồ ghế');

      await dateTabs.nth(0).focus();
      await page.keyboard.press('ArrowRight');

      await expect(dateTabs.nth(1)).toHaveAttribute('aria-selected', 'true');
      expect(page.url()).toContain(`date=${dateKey1}`);

      const panel1 = page.locator(`[data-date-panel="${dateKey1}"]`);
      await expect(panel1).toBeVisible();
      await expect(panel0).toBeHidden();

      const panel1Cards = panel1.locator('[data-testid="showtime-card"]');
      await expect(panel1Cards).toHaveCount(2);
      await expect(panel1Cards.nth(0).locator('[data-testid="showtime-start"]')).toHaveText('00:30');
      await expect(panel1Cards.nth(1).locator('[data-testid="showtime-start"]')).toHaveText('19:00');

      await page.reload({ waitUntil: 'networkidle' });
      await expect(dateTabs.nth(1)).toHaveAttribute('aria-selected', 'true');
      await expect(panel1).toBeVisible();

      await page.goBack({ waitUntil: 'networkidle' });
      await expect(dateTabs.nth(0)).toHaveAttribute('aria-selected', 'true');
      await expect(panel0).toBeVisible();

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
      await page.route('**/media/movies/verified/tears-of-steel-2012-poster-w342.jpg', (route) =>
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

      await page.unroute('**/media/movies/verified/tears-of-steel-2012-poster-w342.jpg');

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
      runRunnerCleanupSafe(['e2e-sql', 'restore-title'], state, 'UI-P3 restore-title');
      await context.close();
    }
  });

  test('7. UI-P3 Verified Movie Detail Stage, Cast Grid, Provenance Dossier, Official Trailer Modal & Suggested On-Sale Movies', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const context = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const page = await context.newPage();

    try {
      await page.route('https://www.youtube-nocookie.com/embed/**', (route) => {
        route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: '<!doctype html><html><body style="background:#000;color:#fff;display:flex;align-items:center;justify-content:center;height:100vh;margin:0">Charade Official Trailer Stub</body></html>',
        });
      });

      // Locate verified film "Mê trong mê" (Charade, 1963) on /movies
      await page.goto('/movies?q=Charade', { waitUntil: 'networkidle' });

      const zeroBootstrapCatalog = await page.evaluate(() => ({
        hasBootstrapCss: Array.from(document.querySelectorAll('link[rel="stylesheet"]')).some((l) =>
          (l.getAttribute('href') || '').includes('bootstrap')
        ),
        hasBootstrapJs: Array.from(document.querySelectorAll('script[src]')).some((s) =>
          (s.getAttribute('src') || '').includes('bootstrap')
        ),
      }));
      expect(zeroBootstrapCatalog.hasBootstrapCss).toBe(false);
      expect(zeroBootstrapCatalog.hasBootstrapJs).toBe(false);

      const charadeCard = page.locator('[data-testid="catalog-movie-card"]', { hasText: 'Mê trong mê' });
      await expect(charadeCard).toBeVisible();
      const charadeCardSrcset = await charadeCard.locator('img.bgp-catalog-poster-img').getAttribute('srcset');
      expect(charadeCardSrcset).toContain('342w');
      expect(charadeCardSrcset).toContain('780w');

      const charadeHref = (await charadeCard.getAttribute('href'))!;
      await page.goto(charadeHref, { waitUntil: 'networkidle' });

      // Zero Bootstrap on /movies/{id}
      const zeroBootstrapDetail = await page.evaluate(() => ({
        hasBootstrapCss: Array.from(document.querySelectorAll('link[rel="stylesheet"]')).some((l) =>
          (l.getAttribute('href') || '').includes('bootstrap')
        ),
        hasBootstrapJs: Array.from(document.querySelectorAll('script[src]')).some((s) =>
          (s.getAttribute('src') || '').includes('bootstrap')
        ),
      }));
      expect(zeroBootstrapDetail.hasBootstrapCss).toBe(false);
      expect(zeroBootstrapDetail.hasBootstrapJs).toBe(false);

      // Verified backdrop + responsive poster srcset + identity + rating + cast + provenance dossier
      await expect(page.locator('[data-testid="detail-backdrop-img"]')).toHaveAttribute('fetchpriority', 'high');
      const detailPosterSrcset = await page.locator('[data-testid="detail-poster-img"]').getAttribute('srcset');
      expect(detailPosterSrcset).toContain('342w');
      expect(detailPosterSrcset).toContain('780w');

      await expect(page.locator('[data-testid="detail-identity"]')).toContainText('Charade');
      await expect(page.locator('[data-testid="detail-identity"]')).toContainText('1963');
      await expect(page.locator('[data-testid="detail-rating-badge"]')).toContainText('IMDb 7.8/10');
      await expect(page.locator('[data-testid="detail-rating-badge"]')).not.toContainText('lượt');
      await expect(page.locator('[data-testid="detail-age-label"]')).toHaveCount(0);
      await expect(page.locator('[data-testid="detail-age-unclassified"]')).toHaveCount(0);
      await expect(page.locator('[data-testid="detail-director"]')).toContainText('Stanley Donen');
      await expect(page.locator('[data-testid="detail-cast-grid"]')).toContainText('Cary Grant');
      await expect(page.locator('[data-testid="detail-cast-grid"]')).toContainText('Audrey Hepburn');

      // Consumer detail page NEVER renders the internal Provenance Dossier card
      await expect(page.locator('[data-testid="detail-provenance-card"]')).toHaveCount(0);
      await expect(page.locator('body')).not.toContainText('Hồ sơ Nguồn Dữ liệu Tác phẩm');

      // Official YouTube Trailer modal on Details page
      const trailerBtn = page.locator('[data-testid="detail-trailer-btn"]');
      await expect(trailerBtn).toBeVisible();
      await trailerBtn.click();

      const dialog = page.locator('[data-testid="trailer-dialog"]');
      await expect(dialog).toBeVisible();
      await expect(dialog.locator('[data-testid="trailer-iframe"]')).toHaveAttribute(
        'src',
        /youtube-nocookie\.com\/embed\/BJ_njww9SOQ/
      );

      await page.click('[data-testid="trailer-dialog-close"]');
      await expect(dialog).toBeHidden();
      await expect(dialog.locator('iframe')).toHaveCount(0);

      // Zero-showtime real movie ("Mai") displays T18 (VN) age classification + suggested on-sale movies (zero provenance card)
      await page.goto('/movies?q=Tr%E1%BA%A5n+Th%C3%A0nh', { waitUntil: 'networkidle' });
      const maiHref = (await page
        .locator('[data-testid="catalog-movie-card"]', { hasText: 'Mai' })
        .getAttribute('href'))!;
      await page.goto(maiHref, { waitUntil: 'networkidle' });

      await expect(page.locator('[data-testid="detail-age-label"]')).toHaveText('T18');
      await expect(page.locator('[data-testid="detail-provenance-card"]')).toHaveCount(0);
      const suggestedBlock = page.locator('[data-testid="detail-suggested-movies"]');
      await expect(suggestedBlock).toBeVisible();
      expect(await suggestedBlock.locator('a.bgp-suggested-card').count()).toBeGreaterThanOrEqual(2);
    } finally {
      await context.close();
    }
  });

  test('8. RapPhim Browse Upgrade (/movies): 5-column auto-submit <select> bar, 6-column desktop grid, IMDb sort, and numbered pagination', async ({
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

      // Verify 5-column select bar & 6-column poster grid at 1440px
      await expect(page.locator('[data-testid="catalog-select-bar"]')).toBeVisible();
      const layoutMetrics = await page.evaluate(() => {
        const selectGrid = document.querySelector('[data-testid="catalog-select-bar"]')!;
        const movieGrid = document.querySelector('[data-testid="catalog-grid"]')!;
        return {
          selectCols: window.getComputedStyle(selectGrid).gridTemplateColumns.split(' ').length,
          movieCols: window.getComputedStyle(movieGrid).gridTemplateColumns.split(' ').length,
        };
      });
      expect(layoutMetrics.selectCols).toBe(5);
      expect(layoutMetrics.movieCols).toBe(6);

      // Auto-submit on Year select -> 1963 filters directly to Charade ("Mê trong mê")
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle' }),
        page.selectOption('[data-testid="catalog-select-year"]', '1963'),
      ]);
      expect(page.url()).toContain('year=1963');
      await expect(page.locator('[data-testid="catalog-movie-card"]')).toHaveCount(1);
      await expect(page.locator('[data-testid="catalog-movie-card"]').first()).toContainText('Mê trong mê');

      // Clear filters via summary reset link
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle' }),
        page.click('[data-testid="catalog-clear-filters"]'),
      ]);
      expect(page.url()).toMatch(/\/movies$/);

      // Auto-submit on Sort select -> rating orders cards by IMDb score descending
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle' }),
        page.selectOption('[data-testid="catalog-select-sort"]', 'rating'),
      ]);
      expect(page.url()).toContain('sort=rating');
      const cardScores = await page.evaluate(() => {
        return Array.from(document.querySelectorAll('[data-testid="catalog-movie-card"] .bgp-catalog-inline-rating'))
          .map((el) => parseFloat((el.textContent || '').replace(/[^\d.]/g, '')))
          .filter((n) => !Number.isNaN(n));
      });
      expect(cardScores.length).toBeGreaterThanOrEqual(4);
      for (let i = 1; i < cardScores.length; i++) {
        expect(cardScores[i - 1]).toBeGreaterThanOrEqual(cardScores[i]);
      }

      // Pagination bar navigates cleanly across pages while preserving sort=rating
      await page.goto('/movies?sort=rating&pageSize=6', { waitUntil: 'networkidle' });
      await expect(page.locator('[data-testid="catalog-movie-card"]')).toHaveCount(6);
      await expect(page.locator('[data-testid="catalog-result-count"]')).toContainText('Trang 1/');

      const page1FirstHref = await page.locator('[data-testid="catalog-movie-card"]').first().getAttribute('href');
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle' }),
        page.click('[data-testid="catalog-page-next"]'),
      ]);
      expect(page.url()).toContain('page=2');
      expect(page.url()).toContain('sort=rating');
      await expect(page.locator('[data-testid="catalog-result-count"]')).toContainText('Trang 2/');
      const page2FirstHref = await page.locator('[data-testid="catalog-movie-card"]').first().getAttribute('href');
      expect(page2FirstHref).not.toBe(page1FirstHref);

      expect(mutatingRequests).toEqual([]);
    } finally {
      await context.close();
    }
  });

  test('9. RapPhim Detail Upgrade (/movies/{id}): 3-column asymmetric workspace, sub-tab switching (Info / Cast / Dossier / Recommended), share toast, and ranked 1->8 sidebar', async ({
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
      await page.goto('/movies?year=1963', { waitUntil: 'networkidle' });
      const charadeHref = (await page
        .locator('[data-testid="catalog-movie-card"]')
        .first()
        .getAttribute('href'))!;
      await page.goto(charadeHref, { waitUntil: 'networkidle' });

      // 1. Verify 3-column asymmetric grid at 1440px (260px Left Dossier + 1fr Center + 304px Ranked Sidebar)
      const workspaceCols = await page.evaluate(() => {
        const ws = document.querySelector('[data-testid="detail-workspace"]')!;
        return window.getComputedStyle(ws).gridTemplateColumns.split(' ').length;
      });
      expect(workspaceCols).toBe(3);

      // 2. Verify Ranked 1->8 "Phim đề xuất" sidebar
      const rankedSidebar = page.locator('[data-testid="detail-ranked-sidebar"]');
      await expect(rankedSidebar).toBeVisible();
      const rankedItems = rankedSidebar.locator('[data-testid="detail-ranked-item"]');
      const rankedCount = await rankedItems.count();
      expect(rankedCount).toBeGreaterThanOrEqual(4);
      expect(rankedCount).toBeLessThanOrEqual(8);
      await expect(rankedItems.nth(0).locator('.bgp-rank-num')).toHaveText('1');
      await expect(rankedItems.nth(1).locator('.bgp-rank-num')).toHaveText('2');
      await expect(rankedItems.nth(2).locator('.bgp-rank-num')).toHaveText('3');

      // 3. Sub-tab switching: Default "info" shows showtimes + cast, with zero provenance card
      await expect(page.locator('[data-testid="detail-tab-info"]')).toHaveAttribute('aria-selected', 'true');
      await expect(page.locator('#showtimes-section')).toBeVisible();
      await expect(page.locator('[data-testid="detail-cast-grid"]')).toBeVisible();
      await expect(page.locator('[data-testid="detail-provenance-card"]')).toHaveCount(0);

      // Switch to "Diễn viên & Đoàn phim" -> isolates Cast card, hides Showtimes
      await page.click('[data-testid="detail-tab-cast"]');
      await expect(page.locator('[data-testid="detail-tab-cast"]')).toHaveAttribute('aria-selected', 'true');
      await expect(page.locator('#showtimes-section')).toBeHidden();
      await expect(page.locator('[data-testid="detail-cast-grid"]')).toBeVisible();

      // Switch to "Trailer" -> reveals inline trailer trigger card cleanly without provenance dossier
      await page.click('[data-testid="detail-tab-trailer"]');
      await expect(page.locator('[data-testid="detail-tab-trailer"]')).toHaveAttribute('aria-selected', 'true');
      await expect(page.locator('[data-detail-card="trailer-inline"]')).toBeVisible();
      await expect(page.locator('[data-testid="detail-provenance-card"]')).toHaveCount(0);
      await expect(page.locator('#showtimes-section')).toBeHidden();

      // Switch to "Đề xuất" -> reveals 2:3 recommended poster grid
      await page.click('[data-testid="detail-tab-recommended"]');
      await expect(page.locator('[data-testid="detail-tab-recommended"]')).toHaveAttribute('aria-selected', 'true');
      await expect(page.locator('[data-testid="detail-recommended-tab-panel"]')).toBeVisible();

      // Clicking Hero CTA "Chọn Suất Chiếu" automatically resets active tab back to "info" & reveals #showtimes-section
      await page.click('[data-testid="detail-cta-scroll-showtimes"]');
      await expect(page.locator('[data-testid="detail-tab-info"]')).toHaveAttribute('aria-selected', 'true');
      await expect(page.locator('#showtimes-section')).toBeVisible();

      // 4. Share button triggers feedback toast
      await page.click('[data-testid="detail-share-btn"]');
      await expect(page.locator('[data-detail-share-feedback]')).toBeVisible();
      await expect(page.locator('[data-detail-share-feedback]')).toContainText('Đã sao chép liên kết');

      expect(mutatingRequests).toEqual([]);
    } finally {
      await context.close();
    }
  });
});
