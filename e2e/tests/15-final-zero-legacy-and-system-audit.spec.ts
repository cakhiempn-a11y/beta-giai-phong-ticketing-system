import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { loadE2eState, runRunnerCommandOrThrow } from '../e2e-env';
import {
  createRoleSession,
  formatVnDateTimeLocal,
  loginInBrowser,
} from './helpers';

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

async function auditPageHealth(page: Page, routeLabel: string) {
  await page.evaluate(async () => {
    await document.fonts.ready;
    // Scroll briefly to trigger any lazy-loaded images, then return to top
    window.scrollTo({ top: document.body.scrollHeight, behavior: 'instant' as ScrollBehavior });
    await new Promise((r) => setTimeout(r, 120));
    window.scrollTo({ top: 0, behavior: 'instant' as ScrollBehavior });
  });

  const audit = await page.evaluate(async () => {
    const links = Array.from(document.querySelectorAll('link[rel="stylesheet"]')).map(
      (l) => l.getAttribute('href') || ''
    );
    const scripts = Array.from(document.querySelectorAll('script[src]')).map(
      (s) => s.getAttribute('src') || ''
    );
    const visibleImgs = Array.from(document.querySelectorAll('img')).filter((img) => {
      const style = window.getComputedStyle(img);
      return style.display !== 'none' && style.visibility !== 'hidden';
    });

    await Promise.all(
      visibleImgs.map(async (img) => {
        if (img.loading === 'lazy' || img.hasAttribute('loading')) {
          img.loading = 'eager';
          img.removeAttribute('loading');
        }
        if (!img.complete) {
          await new Promise<void>((resolve) => {
            const done = () => resolve();
            img.addEventListener('load', done, { once: true });
            img.addEventListener('error', done, { once: true });
            setTimeout(done, 5000);
          });
        }
      })
    );

    const brokenImgs = visibleImgs
      .filter((img) => !img.complete || img.naturalWidth <= 0)
      .map((img) => img.getAttribute('src') || '(empty)');

    const fakePosterRefs = visibleImgs
      .map((img) => img.currentSrc || img.getAttribute('src') || '')
      .filter((src) => /poster-(scifi|horror|animation|noir)\.svg|hero-cinema-backdrop\.svg/i.test(src));

    const bodyText = document.body.innerText || '';
    const hasFakeStreamingClaims =
      /98%\s*phù\s*hợp|Top\s*10\s*tại\s*Việt\s*Nam|Xem\s*phim\s*trực\s*tuyến|Phim\s*mo\s*phong/i.test(bodyText);

    const h1 = document.querySelector('h1') as HTMLElement | null;
    let h1Fg = '';
    let h1Bg = '';
    if (h1) {
      h1Fg = window.getComputedStyle(h1).color;
      let curr: HTMLElement | null = h1;
      while (curr) {
        const bg = window.getComputedStyle(curr).backgroundColor;
        if (bg && bg !== 'transparent' && bg !== 'rgba(0, 0, 0, 0)') {
          h1Bg = bg;
          break;
        }
        curr = curr.parentElement;
      }
      if (!h1Bg) h1Bg = window.getComputedStyle(document.body).backgroundColor;
    }

    return {
      hasBootstrapCss: links.some((h) => h.includes('bootstrap')),
      hasDarkCompatCss: links.some((h) => h.includes('dark-compat')),
      hasLegacyAppShellCss: links.some((h) => h.includes('app-shell.css') || h.includes('design-tokens.css')),
      hasBootstrapJs: scripts.some((s) => s.includes('bootstrap')),
      hasJQuery: scripts.some((s) => s.includes('jquery')),
      brokenImgs,
      fakePosterRefs,
      hasFakeStreamingClaims,
      docOverflowPx: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      h1Fg,
      h1Bg,
    };
  });

  expect(audit.hasBootstrapCss, `${routeLabel}: zero bootstrap CSS`).toBe(false);
  expect(audit.hasDarkCompatCss, `${routeLabel}: zero dark-compat.css`).toBe(false);
  expect(audit.hasLegacyAppShellCss, `${routeLabel}: zero legacy app-shell/design-tokens CSS`).toBe(false);
  expect(audit.hasBootstrapJs, `${routeLabel}: zero bootstrap JS`).toBe(false);
  expect(audit.hasJQuery, `${routeLabel}: zero jQuery`).toBe(false);
  expect(audit.brokenImgs, `${routeLabel}: all visible <img> must have naturalWidth > 0`).toEqual([]);
  expect(audit.fakePosterRefs, `${routeLabel}: zero synthetic poster-*.svg`).toEqual([]);
  expect(audit.hasFakeStreamingClaims, `${routeLabel}: zero fake streaming/fictional claims`).toBe(false);
  expect(audit.docOverflowPx, `${routeLabel}: zero horizontal overflow`).toBeLessThanOrEqual(0);

  if (audit.h1Fg && audit.h1Bg) {
    const ratio = calculateContrast(audit.h1Fg, audit.h1Bg);
    expect(ratio, `${routeLabel}: H1 contrast ratio (${ratio.toFixed(2)}:1) >= 4.5`).toBeGreaterThanOrEqual(4.5);
  }
}

test.describe('UI-P8: Zero-Legacy Bootstrap/jQuery/SVG Cleanup & Full-System Acceptance Audit', () => {
  let activeShowtimeId = 0;
  let activeMovieHref = '';
  let paidOrderId = 0;

  test.beforeAll(async ({ browser }) => {
    const state = loadE2eState();
    runRunnerCommandOrThrow(['seed-verified-showcase'], state, {
      BGP_ALLOW_DEMO_SEED: 'true',
      BGP_DEMO_PASSWORD: state.demoPassword,
    });
    runRunnerCommandOrThrow(['e2e-sql', 'seed-admin'], state);

    const mgrSession = await createRoleSession(browser, state.baseUrl);
    const custSession = await createRoleSession(browser, state.baseUrl);
    try {
      await loginInBrowser(mgrSession.page, 'manager@demo.invalid', state.demoPassword);
      const apiMovies = (await (await mgrSession.page.request.get('/api/movies')).json()) as any[];
      const charade = apiMovies.find((m) => m.provenance?.wikidataQid === 'Q496266') ?? apiMovies[0];
      activeMovieHref = `/movies/${charade.movieId}`;

      // Find or publish a bookable showtime
      const showtimesResp = await mgrSession.page.request.get(`/api/movies/${charade.movieId}/showtimes`);
      const showtimesJson = (await showtimesResp.json()) as any;
      const flatShowtimes: any[] = Array.isArray(showtimesJson)
        ? showtimesJson
        : (showtimesJson.dates ?? []).flatMap((d: any) => d.showtimes ?? []);
      const existing = flatShowtimes.find((s: any) => (s.availableSeats ?? 10) >= 2);
      if (existing) {
        activeShowtimeId = Number(existing.showtimeId);
      } else {
        await mgrSession.page.goto('/management/showtimes/create');
        await mgrSession.page.selectOption('select[name="MovieId"]', String(charade.movieId));
        const opt = mgrSession.page.locator('select[name="RoomId"] option').nth(1);
        const roomId = (await opt.getAttribute('value'))!;
        await mgrSession.page.selectOption('select[name="RoomId"]', roomId);
        await mgrSession.page.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(new Date(Date.now() + 36 * 3600_000)));
        await mgrSession.page.fill('input[name="BufferMinutes"]', '15');
        await mgrSession.page.fill('input[name="Prices[STANDARD]"]', '75000');
        await mgrSession.page.fill('input[name="Prices[VIP]"]', '95000');
        await Promise.all([
          mgrSession.page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          mgrSession.page.click('main button[type="submit"]'),
        ]);
        activeShowtimeId = Number(mgrSession.page.url().match(/\/management\/showtimes\/(\d+)$/)![1]);
      }

      // Create a PAID order so /orders and /orders/{id}/tickets have live QR boarding passes
      await loginInBrowser(custSession.page, 'customer1@demo.invalid', state.demoPassword);
      await custSession.page.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await custSession.page.locator('.seat-box[data-bookable="true"]').first().click();
      await Promise.all([
        custSession.page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custSession.page.click('#btnSubmit'),
      ]);
      paidOrderId = Number(custSession.page.url().match(/\/orders\/(\d+)$/)![1]);
      await custSession.page.goto(`/orders/${paidOrderId}/checkout`, { waitUntil: 'networkidle' });
      await Promise.all([
        custSession.page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custSession.page.click('#btnPaySuccess'),
      ]);
    } finally {
      await mgrSession.context.close();
      await custSession.context.close();
    }
  });

  test('15.1 Repository & HTTP Static Asset Hygiene: legacy Bootstrap, jQuery, dark-compat.css, _Layout.cshtml & synthetic poster-*.svg are permanently removed', async ({
    request,
  }) => {
    const webRoot = path.resolve(__dirname, '..', '..', 'src', 'BetaGiaiPhong.Web');

    const removedPaths = [
      path.join(webRoot, 'Views', 'Shared', '_Layout.cshtml'),
      path.join(webRoot, 'Views', 'Shared', '_Layout.cshtml.css'),
      path.join(webRoot, 'Views', 'Shared', '_ValidationScriptsPartial.cshtml'),
      path.join(webRoot, 'wwwroot', 'lib', 'bootstrap'),
      path.join(webRoot, 'wwwroot', 'lib', 'jquery'),
      path.join(webRoot, 'wwwroot', 'lib', 'jquery-validation'),
      path.join(webRoot, 'wwwroot', 'lib', 'jquery-validation-unobtrusive'),
      path.join(webRoot, 'wwwroot', 'css', 'dark-compat.css'),
      path.join(webRoot, 'wwwroot', 'css', 'app-shell.css'),
      path.join(webRoot, 'wwwroot', 'css', 'design-tokens.css'),
      path.join(webRoot, 'wwwroot', 'css', 'site.css'),
      path.join(webRoot, 'wwwroot', 'js', 'site.js'),
      path.join(webRoot, 'wwwroot', 'images', 'posters'),
      path.join(webRoot, 'wwwroot', 'images', 'hero-cinema-backdrop.svg'),
    ];

    for (const p of removedPaths) {
      expect(fs.existsSync(p), `Legacy path must not exist: ${p}`).toBe(false);
    }

    // Honest neutral fallback placeholder and local WOFF2 fonts MUST exist
    expect(fs.existsSync(path.join(webRoot, 'wwwroot', 'images', 'poster-fallback.svg'))).toBe(true);
    expect(fs.existsSync(path.join(webRoot, 'wwwroot', 'fonts', 'OFL-LICENSE.txt'))).toBe(true);

    // Scan all .cshtml views for zero transitional flags or Bootstrap data attributes
    function collectCshtml(dir: string): string[] {
      const entries = fs.readdirSync(dir, { withFileTypes: true });
      const out: string[] = [];
      for (const e of entries) {
        const full = path.join(dir, e.name);
        if (e.isDirectory()) out.push(...collectCshtml(full));
        else if (e.name.endsWith('.cshtml')) out.push(full);
      }
      return out;
    }

    const cshtmlFiles = collectCshtml(path.join(webRoot, 'Views'));
    expect(cshtmlFiles.length).toBeGreaterThanOrEqual(28); // 26 views + 2 layouts + _ViewStart + _ViewImports
    for (const f of cshtmlFiles) {
      const content = fs.readFileSync(f, 'utf-8');
      expect(content, `${path.basename(f)}: no UseLegacyBootstrap`).not.toContain('UseLegacyBootstrap');
      expect(content, `${path.basename(f)}: no dark-compat.css`).not.toContain('dark-compat.css');
      expect(content, `${path.basename(f)}: no bootstrap.min`).not.toContain('bootstrap.min');
      expect(content, `${path.basename(f)}: no jquery`).not.toContain('jquery');
      expect(content, `${path.basename(f)}: no data-bs-`).not.toContain('data-bs-');
      expect(content, `${path.basename(f)}: no synthetic poster-*.svg`).not.toMatch(
        /poster-(scifi|horror|animation|noir)\.svg/
      );
    }

    // Verify HTTP 404 on legacy static asset URLs
    for (const deadUrl of [
      '/lib/bootstrap/dist/css/bootstrap.min.css',
      '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
      '/lib/jquery/dist/jquery.min.js',
      '/css/dark-compat.css',
      '/images/posters/poster-scifi.svg',
      '/images/hero-cinema-backdrop.svg',
    ]) {
      const resp = await request.get(deadUrl);
      expect(resp.status(), `${deadUrl} must return 404`).toBe(404);
    }
  });

  test('15.2 Full-Surface Visual, Image Integrity (naturalWidth > 0), Provenance Attribution & Reduced-Motion Audit across Desktop & Mobile (320px/390px)', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const custSession = await createRoleSession(browser, state.baseUrl);
    const staffSession = await createRoleSession(browser, state.baseUrl);
    const mgrSession = await createRoleSession(browser, state.baseUrl);

    try {
      await loginInBrowser(custSession.page, 'customer1@demo.invalid', state.demoPassword);
      await loginInBrowser(staffSession.page, 'staff@demo.invalid', state.demoPassword);
      await loginInBrowser(mgrSession.page, 'manager@demo.invalid', state.demoPassword);

      const consumerRoutes = [
        '/',
        '/movies',
        activeMovieHref,
        `/showtimes/${activeShowtimeId}/seats`,
        `/orders/${paidOrderId}/tickets`,
        '/orders',
        '/account/profile',
        '/privacy',
      ];

      const staffRoutes = ['/staff/check-in', '/staff/check-in/history'];
      const mgrRoutes = [
        '/management/reports',
        '/management/movies',
        '/management/showtimes',
        '/management/rooms',
      ];

      for (const vp of [
        { width: 1440, height: 900 },
        { width: 390, height: 844 },
        { width: 320, height: 640 },
      ]) {
        await custSession.page.setViewportSize(vp);
        for (const r of consumerRoutes) {
          await custSession.page.goto(r, { waitUntil: 'networkidle' });
          await auditPageHealth(custSession.page, `CUSTOMER ${r} @${vp.width}px`);
        }

        await staffSession.page.setViewportSize(vp);
        for (const r of staffRoutes) {
          await staffSession.page.goto(r, { waitUntil: 'networkidle' });
          await auditPageHealth(staffSession.page, `STAFF ${r} @${vp.width}px`);
        }

        await mgrSession.page.setViewportSize(vp);
        for (const r of mgrRoutes) {
          await mgrSession.page.goto(r, { waitUntil: 'networkidle' });
          await auditPageHealth(mgrSession.page, `MANAGER ${r} @${vp.width}px`);
        }
      }

      // Verify required open-data & attribution notices in Consumer Footer & /privacy
      await custSession.page.setViewportSize({ width: 1440, height: 900 });
      await custSession.page.goto('/privacy', { waitUntil: 'networkidle' });
      const privacyText = await custSession.page.locator('body').innerText();
      expect(privacyText).toContain('This product uses the TMDB API');
      expect(privacyText).toContain('IMDb Non-Commercial Datasets');
      expect(privacyText).toContain('Wikimedia Commons');
      expect(privacyText).toContain('Wikidata');
      expect(privacyText).toMatch(/CC BY 4\.0|Creative Commons/);

      // Verify @media (prefers-reduced-motion: reduce) collapses transition durations
      await custSession.page.emulateMedia({ reducedMotion: 'reduce' });
      await custSession.page.goto('/', { waitUntil: 'networkidle' });
      const headerTransitionMs = await custSession.page
        .locator('[data-testid="cinema-header"]')
        .evaluate((el) => window.getComputedStyle(el).transitionDuration);
      // Under prefers-reduced-motion: reduce in 02-base.css, transition-duration is <= 0.01ms (0.00001s / 0s)
      expect(['0s', '0.00001s', '1e-05s'].some((v) => headerTransitionMs.startsWith(v) || parseFloat(headerTransitionMs) <= 0.01)).toBe(true);
    } finally {
      await custSession.context.close();
      await staffSession.context.close();
      await mgrSession.context.close();
    }
  });
});
