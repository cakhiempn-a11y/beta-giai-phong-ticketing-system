import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { loadE2eState, runRunnerCommandOrThrow } from '../e2e-env';
import {
  assertNoSecretLeakInText,
  createRoleSession,
  formatVnDateTimeLocal,
  getOrderSensitiveQrArtifactsInMemory,
  loginInBrowser,
} from './helpers';

async function assertZeroBootstrapOpsSurface(page: Page, routeLabel: string) {
  const audit = await page.evaluate(() => {
    const bodyBg = window.getComputedStyle(document.body).backgroundColor;
    const hasLegacyCompatClass = document.body.classList.contains('bgp-legacy-compat');
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
      hasLegacyCompatClass,
      hasBootstrapCss: links.some((h) => h.includes('bootstrap')),
      hasDarkCompatCss: links.some((h) => h.includes('dark-compat')),
      hasPageOpsCss: links.some((h) => h.includes('page-ops.css')),
      hasBootstrapJs: scripts.some((s) => s.includes('bootstrap')),
      hasJQuery: scripts.some((s) => s.includes('jquery')),
      hasWindowJQuery: typeof (window as any).jQuery !== 'undefined',
      hasBootstrapBlue,
    };
  });

  expect(audit.bodyBg, `${routeLabel}: body background`).toBe('rgb(11, 11, 14)');
  expect(audit.hasLegacyCompatClass, `${routeLabel}: no bgp-legacy-compat`).toBe(false);
  expect(audit.hasBootstrapCss, `${routeLabel}: zero bootstrap.min.css`).toBe(false);
  expect(audit.hasDarkCompatCss, `${routeLabel}: zero dark-compat.css`).toBe(false);
  expect(audit.hasPageOpsCss, `${routeLabel}: loads page-ops.css`).toBe(true);
  expect(audit.hasBootstrapJs, `${routeLabel}: zero bootstrap.bundle.js`).toBe(false);
  expect(audit.hasJQuery, `${routeLabel}: zero jquery.js`).toBe(false);
  expect(audit.hasWindowJQuery, `${routeLabel}: zero window.jQuery`).toBe(false);
  expect(audit.hasBootstrapBlue, `${routeLabel}: zero Bootstrap blue`).toBe(false);
}

test.describe('UI-P7: High-Density Operations Console (STAFF / MANAGER / ADMIN) & Controlled External Provenance Sync', () => {
  let scannableShowtimeId = 0;
  let showcaseMovieId = 0;
  let sampleRoomId = 0;

  test.beforeAll(async ({ browser }) => {
    const state = loadE2eState();
    runRunnerCommandOrThrow(['seed-verified-showcase'], state);

    const mgrSession = await createRoleSession(browser, state.baseUrl);
    try {
      const mgrPage = mgrSession.page;
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);

      const moviesResp = await mgrPage.request.get('/api/movies');
      const movies = (await moviesResp.json()) as any[];
      const charade = movies.find((m) => m.provenance?.wikidataQid === 'Q496266') ?? movies[0];
      expect(charade).toBeTruthy();
      showcaseMovieId = Number(charade.movieId);

      // Publish an ON_SALE showtime inside Staff check-in window (StartUtc = now + 18m) if a room is free
      const candidateRooms = ['Phòng 02', 'Phòng 03', 'Phòng 01'];
      for (const roomLabel of candidateRooms) {
        await mgrPage.goto('/management/showtimes/create');
        await mgrPage.selectOption('select[name="MovieId"]', String(showcaseMovieId));

        const roomOpt = mgrPage.locator('select[name="RoomId"] option', { hasText: roomLabel }).first();
        if ((await roomOpt.count()) === 0) continue;
        const rId = (await roomOpt.getAttribute('value'))!;
        sampleRoomId = Number(rId);
        await mgrPage.selectOption('select[name="RoomId"]', rId);

        const startUtcTarget = new Date(Date.now() + 18 * 60 * 1000);
        await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(startUtcTarget));
        await mgrPage.fill('input[name="BufferMinutes"]', '15');
        await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
        const vipInput = mgrPage.locator('input[name="Prices[VIP]"]');
        if ((await vipInput.count()) > 0) await vipInput.fill('95000');
        const sweetInput = mgrPage.locator('input[name="Prices[SWEETBOX]"]');
        if ((await sweetInput.count()) > 0) await sweetInput.fill('160000');

        await Promise.all([
          mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          mgrPage.click('main button[type="submit"]'),
        ]);

        const match = mgrPage.url().match(/\/management\/showtimes\/(\d+)$/);
        if (match) {
          scannableShowtimeId = Number(match[1]);
          break;
        }
      }

      // If all active rooms already have an ON_SALE showtime in the current 30-min check-in window (from earlier E2E specs),
      // pick the existing scannable + bookable ON_SALE showtime directly!
      if (scannableShowtimeId === 0) {
        await mgrPage.goto('/staff/check-in', { waitUntil: 'networkidle' });
        const optionValues = await mgrPage
          .locator('#showtimeSelect option')
          .evaluateAll((opts) => opts.map((o) => o.getAttribute('value') || '').filter(Boolean));

        for (const val of optionValues) {
          const candidateId = Number(val);
          if (!candidateId) continue;
          await mgrPage.goto(`/showtimes/${candidateId}/seats`, { waitUntil: 'networkidle' });
          if ((await mgrPage.locator('.seat-box[data-bookable="true"]').count()) > 0) {
            scannableShowtimeId = candidateId;
            break;
          }
        }
      }

      expect(scannableShowtimeId).toBeGreaterThan(0);
      expect(sampleRoomId).toBeGreaterThan(0);
    } finally {
      await mgrSession.context.close();
    }
  });

  test('14.1 Zero-Bootstrap, Zero-jQuery & RBAC boundaries across all 12 operational routes', async ({ browser }) => {
    const state = loadE2eState();
    const guestSession = await createRoleSession(browser, state.baseUrl);
    const custSession = await createRoleSession(browser, state.baseUrl);
    const staffSession = await createRoleSession(browser, state.baseUrl);
    const mgrSession = await createRoleSession(browser, state.baseUrl);

    try {
      await loginInBrowser(custSession.page, 'customer1@demo.invalid', state.demoPassword);
      await loginInBrowser(staffSession.page, 'staff@demo.invalid', state.demoPassword);
      await loginInBrowser(mgrSession.page, 'manager@demo.invalid', state.demoPassword);

      // 1. RBAC enforcement on controlled external lookup API & management routes
      const guestLookup = await guestSession.context.request.get(
        '/api/management/movies/external-lookup?query=tt0807840',
        { maxRedirects: 0 }
      );
      expect([302, 401]).toContain(guestLookup.status());

      const custLookup = await custSession.context.request.get(
        '/api/management/movies/external-lookup?query=tt0807840',
        { maxRedirects: 0 }
      );
      expect([302, 403]).toContain(custLookup.status());

      const staffLookup = await staffSession.context.request.get(
        '/api/management/movies/external-lookup?query=tt0807840',
        { maxRedirects: 0 }
      );
      expect([302, 403]).toContain(staffLookup.status());

      await staffSession.page.goto('/management/movies');
      await expect(staffSession.page).toHaveURL(/\/account\/access-denied/);

      // 2. STAFF routes (2 views)
      for (const staffRoute of ['/staff/check-in', '/staff/check-in/history']) {
        await staffSession.page.goto(staffRoute, { waitUntil: 'networkidle' });
        await assertZeroBootstrapOpsSurface(staffSession.page, `STAFF ${staffRoute}`);
      }

      // 3. MANAGER routes (all 10 views)
      const managerRoutes = [
        '/management/reports',
        '/management/movies',
        '/management/movies/create',
        `/management/movies/${showcaseMovieId}`,
        `/management/movies/${showcaseMovieId}/edit`,
        '/management/showtimes',
        '/management/showtimes/create',
        `/management/showtimes/${scannableShowtimeId}`,
        '/management/rooms',
        `/management/rooms/${sampleRoomId}`,
      ];

      for (const mgrRoute of managerRoutes) {
        await mgrSession.page.goto(mgrRoute, { waitUntil: 'networkidle' });
        await assertZeroBootstrapOpsSurface(mgrSession.page, `MANAGER ${mgrRoute}`);
      }

      for (const s of [guestSession, custSession, staffSession, mgrSession]) {
        expect(s.pageErrors).toEqual([]);
      }
    } finally {
      await guestSession.context.close();
      await custSession.context.close();
      await staffSession.context.close();
      await mgrSession.context.close();
    }
  });

  test('14.2 STAFF High-Speed Gate Check-In Console (/staff/check-in & /staff/check-in/history): optical QR file decode, sound toggle, manual token & zero secret leaks', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const custSession = await createRoleSession(browser, state.baseUrl);
    const staffSession = await createRoleSession(browser, state.baseUrl);
    const tmpDir = path.join(__dirname, '..', '.tmp');
    if (!fs.existsSync(tmpDir)) fs.mkdirSync(tmpDir, { recursive: true });
    const qrCapturePath = path.join(tmpDir, `uip7-qr-${Date.now()}.png`);

    let alertTriggered = false;
    staffSession.page.on('dialog', async (d) => {
      alertTriggered = true;
      await d.dismiss();
    });

    try {
      await loginInBrowser(custSession.page, 'customer1@demo.invalid', state.demoPassword);
      await loginInBrowser(staffSession.page, 'staff@demo.invalid', state.demoPassword);

      // Customer reserves 1 seat & completes mock payment to obtain a real rendered QR boarding pass
      const custPage = custSession.page;
      await custPage.goto(`/showtimes/${scannableShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await custPage.locator('.seat-box[data-bookable="true"]').first().click();
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnSubmit'),
      ]);
      const orderId = Number(custPage.url().match(/\/orders\/(\d+)$/)![1]);

      await custPage.goto(`/orders/${orderId}/checkout`, { waitUntil: 'networkidle' });
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnPaySuccess'),
      ]);

      await custPage.goto(`/orders/${orderId}/tickets`, { waitUntil: 'networkidle' });
      const qrWell = custPage.locator('[data-testid="ticket-qr-box"]').first();
      await expect(qrWell).toBeVisible();
      await qrWell.screenshot({ path: qrCapturePath });

      // STAFF opens Gate Check-In Console
      const staffPage = staffSession.page;
      await staffPage.goto(`/staff/check-in?showtimeId=${scannableShowtimeId}`, { waitUntil: 'networkidle' });
      await expect(staffPage.locator('#showtimeSelect')).toHaveValue(String(scannableShowtimeId));

      // Sound feedback toggle button works cleanly via aria-pressed
      const soundBtn = staffPage.locator('#btnToggleSound');
      await expect(soundBtn).toBeVisible();
      await expect(soundBtn).toHaveAttribute('aria-pressed', 'false');
      await soundBtn.click();
      await expect(soundBtn).toHaveAttribute('aria-pressed', 'true');
      await soundBtn.click();
      await expect(soundBtn).toHaveAttribute('aria-pressed', 'false');

      // Custom styled QR file upload trigger is visible while native file input is SR-accessible
      await expect(staffPage.locator('label[for="qrFileInput"].bgp-qr-upload-btn')).toBeVisible();

      // 1. Optical QR upload -> HỢP LỆ
      await staffPage.locator('#qrFileInput').setInputFiles(qrCapturePath);
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('HỢP LỆ', { timeout: 15_000 });
      await expect(staffPage.locator('#ticketDetailsCard')).toBeVisible();
      const ticketCode = (await staffPage.locator('#resTicketCode').textContent())?.trim() || '';
      expect(ticketCode.length).toBeGreaterThan(0);

      // 2. Rescan current ticket -> ĐÃ SỬ DỤNG
      await expect(staffPage.locator('#btnRescanCurrent')).toBeVisible();
      await staffPage.click('#btnRescanCurrent');
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('ĐÃ SỬ DỤNG', { timeout: 15_000 });

      // 3. Reset via #btnScanNext & test manual USB/token fallback -> KHÔNG TỒN TẠI
      await staffPage.click('#btnScanNext');
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('Sẵn sàng quét');
      await staffPage.fill('#manualQrToken', 'BGP_NONEXISTENT_MANUAL_TOKEN_UIP7_TEST_00001');
      await staffPage.click('#btnManualSubmit');
      await expect(staffPage.locator('#scanStatusBadge')).toHaveText('KHÔNG TỒN TẠI', { timeout: 15_000 });

      // 4. Verify dense Gate Audit History (/staff/check-in/history)
      await staffPage.goto(`/staff/check-in/history?showtimeId=${scannableShowtimeId}`, { waitUntil: 'networkidle' });
      await expect(staffPage.locator('table')).toContainText('HỢP LỆ (ACCEPTED)');
      await expect(staffPage.locator('table')).toContainText('ĐÃ DÙNG (ALREADY_USED)');
      await expect(staffPage.locator('table')).toContainText(ticketCode);

      await staffPage.goto('/staff/check-in/history?outcome=NOT_FOUND', { waitUntil: 'networkidle' });
      await expect(staffPage.locator('table')).toContainText('KHÔNG TỒN TẠI (NOT_FOUND)');

      // 5. Security invariants: zero alert(), zero raw QR token leaks
      expect(alertTriggered).toBe(false);
      const sensitiveQr = getOrderSensitiveQrArtifactsInMemory(orderId, state);
      const historyHtml = await staffPage.content();
      assertNoSecretLeakInText(historyHtml, state, 'Staff Check-In History HTML', sensitiveQr);
      assertNoSecretLeakInText(staffSession.consoleMessages.join('\n'), state, 'Staff Console Logs', sensitiveQr);

      expect(custSession.pageErrors).toEqual([]);
      expect(staffSession.pageErrors).toEqual([]);
    } finally {
      if (fs.existsSync(qrCapturePath)) {
        fs.unlinkSync(qrCapturePath);
      }
      await custSession.context.close();
      await staffSession.context.close();
    }
  });

  test('14.3 MANAGER Controlled External Provenance Lookup & Sync, ManualOverrideMask locks, autofill & <dialog> confirmations', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const mgrSession = await createRoleSession(browser, state.baseUrl);
    let nativeDialogCount = 0;
    mgrSession.page.on('dialog', async (d) => {
      nativeDialogCount += 1;
      await d.dismiss();
    });

    try {
      const mgrPage = mgrSession.page;
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);

      // 1. Open Controlled External Lookup Drawer on /management/movies
      await mgrPage.goto('/management/movies', { waitUntil: 'networkidle' });
      await expect(mgrPage.locator('[data-testid="tmdb-env-status"]')).toBeVisible();

      const toggleBtn = mgrPage.locator('[data-testid="toggle-external-lookup-btn"]');
      await toggleBtn.click();
      const drawer = mgrPage.locator('[data-testid="external-lookup-drawer"]');
      await expect(drawer).toBeVisible();

      // 2. Graceful handling when querying an uncached numeric TMDB ID without BGP_TMDB_READ_TOKEN (zero 500)
      await drawer.locator('[data-testid="external-lookup-input"]').fill('999888777');
      await drawer.locator('[data-sync-search-btn]').click();
      await expect(drawer.locator('[data-sync-status]')).toBeVisible();
      await expect(drawer.locator('[data-sync-status]')).toContainText(/TMDB|Không tìm thấy/i);

      // 3. Lookup verified Blender Open Movie: Elephants Dream (2006) via IMDb ID tt0807840
      await drawer.locator('[data-testid="external-lookup-input"]').fill('tt0807840');
      await drawer.locator('[data-sync-search-btn]').click();

      const candidate = drawer.locator('[data-testid="external-lookup-item"]').first();
      await expect(candidate).toBeVisible();
      await expect(candidate).toContainText('Elephants Dream (2006)');
      await expect(candidate).toContainText('Q737719');
      await expect(candidate).toContainText('tt0807840');
      await expect(candidate).toContainText('TMDB #39079');
      await expect(candidate).toContainText('11 phút');

      // Import as DRAFT (or re-open if already imported)
      const importBtn = candidate.locator('[data-testid="import-external-draft-btn"], [data-testid="resync-external-btn"]').first();
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        importBtn.click(),
      ]);

      await expect(mgrPage).toHaveURL(/\/management\/movies\/(\d+)$/);
      const importedMovieId = Number(mgrPage.url().match(/\/management\/movies\/(\d+)$/)![1]);

      // Verify DRAFT status + real provenance + NO auto-converted foreign age rating
      await expect(mgrPage.locator('.badge', { hasText: 'DRAFT' }).first()).toBeVisible();
      await expect(mgrPage.locator('[data-testid="movie-provenance-card"]')).toContainText('Q737719');
      await expect(mgrPage.locator('[data-testid="movie-provenance-card"]')).toContainText('tt0807840');
      await expect(mgrPage.locator('body')).toContainText('Chưa phân loại độ tuổi VN');

      // 4. Edit movie on /management/movies/{id}/edit: verify auto-lock checkboxes & save VN title + T13
      await mgrPage.goto(`/management/movies/${importedMovieId}/edit`, { waitUntil: 'networkidle' });
      const titleLockCheckbox = mgrPage.locator('input[name="ManualOverrides"][value="title"]');
      const ageLockCheckbox = mgrPage.locator('input[name="ManualOverrides"][value="age_label"]');

      const vnReleaseTitle = 'Giấc Mơ Voi Máy (Elephants Dream)';
      await mgrPage.fill('input[name="Title"]', vnReleaseTitle);
      await mgrPage.selectOption('select[name="AgeLabel"]', 'T13');

      // ops-management.js automatically enables override locks for edited fields!
      await expect(titleLockCheckbox).toBeChecked();
      await expect(ageLockCheckbox).toBeChecked();

      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);

      await expect(mgrPage).toHaveURL(new RegExp(`/management/movies/${importedMovieId}$`));
      await expect(mgrPage.locator('h1')).toHaveText(vnReleaseTitle);
      await expect(mgrPage.locator('[data-testid="active-override-mask"]')).toContainText('title');
      await expect(mgrPage.locator('[data-testid="active-override-mask"]')).toContainText('age_label');

      // 5. Re-sync from verified external source -> locked Title & T13 MUST NOT be overwritten!
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('[data-testid="detail-resync-external-btn"]'),
      ]);
      await expect(mgrPage.locator('h1')).toHaveText(vnReleaseTitle);
      await expect(mgrPage.locator('.bgp-ops-age', { hasText: 'T13' }).first()).toBeVisible();
      await expect(mgrPage.locator('[data-testid="movie-provenance-card"]')).toContainText('Q737719');

      // 6. Verify /management/movies/create autofill helper (Big Buck Bunny Q731114) without submitting duplicate
      await mgrPage.goto('/management/movies/create', { waitUntil: 'networkidle' });
      await mgrPage.fill('[data-sync-search-input]', 'Q731114');
      await mgrPage.click('[data-sync-search-btn]');
      const fillBtn = mgrPage.locator('[data-testid="autofill-verified-btn"]').first();
      await expect(fillBtn).toBeVisible();
      await fillBtn.click();
      await expect(mgrPage.locator('input[name="Title"]')).toHaveValue('Big Buck Bunny');
      await expect(mgrPage.locator('input[name="DurationMin"]')).toHaveValue('10');
      await expect(mgrPage.locator('input[name="WikidataQid"]')).toHaveValue('Q731114');
      await expect(mgrPage.locator('select[name="AgeLabel"]')).toHaveValue('');

      // 7. Publish importedMovieId -> schedule future showtime -> test <dialog> Close, Cancel & Archive
      await mgrPage.goto(`/management/movies/${importedMovieId}`, { waitUntil: 'networkidle' });
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click(`form[action*="/management/movies/${importedMovieId}/publish"] button[type="submit"]`),
      ]);
      await expect(mgrPage.locator('.badge', { hasText: 'PUBLISHED' }).first()).toBeVisible();

      // Click "+ Lên lịch suất chiếu" (verifies ?movieId= preselection on ShowtimeCreate)
      await mgrPage.click(`a[href*="/management/showtimes/create?movieId=${importedMovieId}"]`);
      await expect(mgrPage.locator('select[name="MovieId"]')).toHaveValue(String(importedMovieId));

      let publishedFutureShowtime = false;
      for (const dayOffset of [19, 23, 27]) {
        if (!mgrPage.url().includes('/management/showtimes/create')) {
          await mgrPage.goto(`/management/showtimes/create?movieId=${importedMovieId}`);
        }
        await mgrPage.selectOption('select[name="MovieId"]', String(importedMovieId));
        await mgrPage.selectOption('select[name="RoomId"]', String(sampleRoomId));
        const futureSlot = new Date(Date.now() + dayOffset * 86400_000);
        await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(futureSlot));
        await mgrPage.fill('input[name="BufferMinutes"]', '15');
        await mgrPage.fill('input[name="Prices[STANDARD]"]', '70000');
        const vipInput = mgrPage.locator('input[name="Prices[VIP]"]');
        if ((await vipInput.count()) > 0 && !(await vipInput.isDisabled())) {
          await vipInput.fill('90000');
        }
        const sweetInput = mgrPage.locator('input[name="Prices[SWEETBOX]"]');
        if ((await sweetInput.count()) > 0 && !(await sweetInput.isDisabled())) {
          await sweetInput.fill('150000');
        }

        await Promise.all([
          mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          mgrPage.click('main button[type="submit"]'),
        ]);
        if (/\/management\/showtimes\/(\d+)$/.test(mgrPage.url())) {
          publishedFutureShowtime = true;
          break;
        }
      }
      expect(publishedFutureShowtime).toBe(true);

      // Close showtime via HTML5 <dialog>
      await mgrPage.click('[data-testid="trigger-close-showtime-btn"]');
      await expect(mgrPage.locator('[data-testid="close-showtime-dialog"]')).toBeVisible();
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('[data-testid="confirm-close-showtime-btn"]'),
      ]);
      await expect(mgrPage.locator('.badge', { hasText: /CLOSED/ })).toBeVisible();

      // Cancel showtime (0 sold tickets) via HTML5 <dialog>
      await mgrPage.click('[data-testid="trigger-cancel-showtime-btn"]');
      await expect(mgrPage.locator('[data-testid="cancel-showtime-dialog"]')).toBeVisible();
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('[data-testid="confirm-cancel-showtime-btn"]'),
      ]);
      await expect(mgrPage.locator('.badge', { hasText: /CANCELLED/ })).toBeVisible();

      // Archive movie via HTML5 <dialog> now that active future showtimes are cancelled
      await mgrPage.goto(`/management/movies/${importedMovieId}`, { waitUntil: 'networkidle' });
      await mgrPage.click(`form[action*="/management/movies/${importedMovieId}/archive"] button[type="submit"]`);
      await expect(mgrPage.locator('[data-testid="archive-movie-dialog"]')).toBeVisible();
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('[data-testid="confirm-archive-movie-btn"]'),
      ]);
      await expect(mgrPage.locator('.badge', { hasText: 'ARCHIVED' }).first()).toBeVisible();

      expect(nativeDialogCount, 'Must use accessible <dialog> instead of window.confirm/alert').toBe(0);
      expect(mgrSession.consoleMessages.filter((m) => m.startsWith('[error]'))).toEqual([]);
      expect(mgrSession.pageErrors).toEqual([]);
    } finally {
      await mgrSession.context.close();
    }
  });

  test('14.4 Responsive 0px horizontal overflow across 320px, 390px, 768px & 1440px on STAFF and MANAGER workspaces', async ({
    browser,
  }) => {
    const state = loadE2eState();
    const staffSession = await createRoleSession(browser, state.baseUrl);
    const mgrSession = await createRoleSession(browser, state.baseUrl);

    const viewports = [
      { width: 320, height: 568 },
      { width: 390, height: 844 },
      { width: 768, height: 1024 },
      { width: 1440, height: 900 },
    ];

    try {
      await loginInBrowser(staffSession.page, 'staff@demo.invalid', state.demoPassword);
      await loginInBrowser(mgrSession.page, 'manager@demo.invalid', state.demoPassword);

      for (const vp of viewports) {
        await staffSession.page.setViewportSize(vp);
        for (const route of ['/staff/check-in', '/staff/check-in/history']) {
          await staffSession.page.goto(route, { waitUntil: 'networkidle' });
          const overflow = await staffSession.page.evaluate(
            () => document.documentElement.scrollWidth - window.innerWidth
          );
          expect(overflow, `${route} at ${vp.width}px horizontal overflow`).toBeLessThanOrEqual(0);
        }

        await mgrSession.page.setViewportSize(vp);
        for (const route of [
          '/management/reports',
          '/management/movies',
          '/management/showtimes',
          '/management/rooms',
        ]) {
          await mgrSession.page.goto(route, { waitUntil: 'networkidle' });
          const overflow = await mgrSession.page.evaluate(
            () => document.documentElement.scrollWidth - window.innerWidth
          );
          expect(overflow, `${route} at ${vp.width}px horizontal overflow`).toBeLessThanOrEqual(0);
        }
      }

      expect(staffSession.pageErrors).toEqual([]);
      expect(mgrSession.pageErrors).toEqual([]);
    } finally {
      await staffSession.context.close();
      await mgrSession.context.close();
    }
  });
});
