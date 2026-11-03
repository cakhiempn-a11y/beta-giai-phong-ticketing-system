import { test, expect } from '@playwright/test';

test.describe('BE/DATA-P2: Verified Real Movie Content, Local Media Pipeline & Quick-Preview API', () => {
  test('10.1 Verified Showcase API & Media Integrity: 11 real films, zero fictional entries, valid local multi-size assets, and honest rating display', async ({
    request,
  }) => {
    const resp = await request.get('/api/movies');
    expect(resp.ok()).toBe(true);
    const movies = (await resp.json()) as any[];

    expect(movies.length).toBeGreaterThanOrEqual(11);
    for (const m of movies) {
      expect(String(m.title)).not.toContain('Phim mo phong');
    }

    const charade = movies.find((m) => m.provenance?.wikidataQid === 'Q496266');
    expect(charade).toBeTruthy();
    expect(charade.title).toBe('Mê trong mê');
    expect(charade.originalTitle).toBe('Charade');
    expect(charade.releaseYear).toBe(1963);
    expect(charade.directorNames).toBe('Stanley Donen');
    expect(charade.officialTrailerYoutubeId).toBe('BJ_njww9SOQ');
    expect(charade.isHeroEligible).toBe(true);
    expect(charade.rating?.source).toBe('IMDB');
    expect(charade.rating?.score).toBe(7.8);
    expect(charade.rating?.scaleMax).toBe(10);
    expect(charade.rating?.voteCount).toBe(95223);
    expect(charade.rating?.displayText).toContain('IMDb 7.8/10');
    expect(charade.ageLabel).toBeNull();

    // Verify local responsive artwork variants exist and return 200 image bytes
    for (const assetUrl of [
      charade.artwork.posterUrl,
      charade.artwork.posterW342Url,
      charade.artwork.backdropUrl,
      charade.artwork.backdropW780Url,
    ]) {
      expect(assetUrl).toMatch(/^\/media\/movies\/verified\//);
      const assetResp = await request.get(assetUrl);
      expect(assetResp.status()).toBe(200);
      const body = await assetResp.body();
      expect(body.byteLength).toBeGreaterThan(2000);
    }

    // Verify Vietnamese films without static commercial rights use PENDING_TMDB_API_KEY and are excluded from Hero
    const mai = movies.find((m) => m.provenance?.wikidataQid === 'Q124516759');
    expect(mai).toBeTruthy();
    expect(mai.ageLabel).toBe('T18');
    expect(mai.provenance?.ageLabelMarket).toBe('VN');
    expect(mai.artwork?.source).toBe('PENDING_TMDB_API_KEY');
    expect(mai.posterUrl).toBeNull();
    expect(mai.isHeroEligible).toBe(false);

    // Verify quick-preview endpoint for published vs non-existent movie
    const previewResp = await request.get(`/api/movies/${charade.movieId}/quick-preview`);
    expect(previewResp.status()).toBe(200);
    const preview = await previewResp.json();
    expect(preview.movieId).toBe(charade.movieId);
    expect(preview.originalTitle).toBe('Charade');
    expect(preview.officialTrailerYoutubeId).toBe('BJ_njww9SOQ');
    expect(preview.rating?.displayText).toContain('IMDb 7.8/10');

    const missingPreview = await request.get('/api/movies/99999999/quick-preview');
    expect(missingPreview.status()).toBe(404);
  });

  test('10.2 Grounded UI Rendering: Hero features verified artwork film, catalogue renders neutral placeholder for PENDING_TMDB_API_KEY, and search matches OriginalTitle & DirectorNames', async ({
    page,
  }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    const heroTitle = await page.locator('.bgp-hero-title').innerText();
    expect(heroTitle).not.toBe('Mai');
    expect(heroTitle).not.toBe('Đào, phở và piano');
    expect(heroTitle).not.toContain('Phim mo phong');

    const heroImg = page.locator('[data-testid="hero-backdrop-img"]');
    await expect(heroImg).toBeVisible();
    await expect(heroImg).toHaveAttribute('fetchpriority', 'high');
    const heroImgSrc = await heroImg.getAttribute('src');
    expect(heroImgSrc).toMatch(/^\/media\/movies\/verified\//);

    // Catalogue renders neutral placeholder (/images/poster-fallback.svg) for Mai
    await page.goto('/movies?q=Tr%E1%BA%A5n+Th%C3%A0nh', { waitUntil: 'networkidle' });
    const maiCard = page.locator('[data-testid="catalog-movie-card"]', { hasText: 'Mai' });
    await expect(maiCard).toBeVisible();
    const maiImg = maiCard.locator('img.bgp-catalog-poster-img');
    await expect(maiImg).toHaveAttribute('src', '/images/poster-fallback.svg');

    // Search by OriginalTitle ("Charade") locates "Mê trong mê"
    await page.goto('/movies?q=Charade', { waitUntil: 'networkidle' });
    const charadeCard = page.locator('[data-testid="catalog-movie-card"]', { hasText: 'Mê trong mê' });
    await expect(charadeCard).toBeVisible();
  });
});
