import { chromium, Browser } from '@playwright/test';
import * as path from 'path';
import * as fs from 'fs';
import { spawnSync } from 'child_process';
import {
  createInitialE2eState,
  runGlobalSetupLifecycle,
  runGlobalTeardownLifecycle,
  E2eState,
  runRunnerCommandOrThrow,
  sanitizeTextForLog,
} from '../e2e-env';
import { formatVnDateTimeLocal, loginInBrowser } from '../tests/helpers';

/**
 * Parses connection string to extract server, authentication mode, and credentials.
 */
function parseSqlConnectionString(connStr: string): {
  server: string;
  user?: string;
  password?: string;
  trusted: boolean;
} {
  const parts = connStr.split(';').filter(Boolean);
  let server = 'localhost';
  let user: string | undefined;
  let password: string | undefined;
  let trusted = true;

  for (const part of parts) {
    const [key, ...vals] = part.split('=');
    const k = key.trim().toLowerCase();
    const v = vals.join('=').trim();
    if (k === 'server' || k === 'data source' || k === 'addr' || k === 'address') {
      server = v;
    } else if (k === 'user id' || k === 'uid' || k === 'user') {
      user = v;
      trusted = false;
    } else if (k === 'password' || k === 'pwd') {
      password = v;
    } else if (k === 'integrated security' || k === 'trusted_connection') {
      trusted = v.toLowerCase() === 'true' || v.toLowerCase() === 'sspi';
    }
  }

  return { server, user, password, trusted };
}

/**
 * Executes an arbitrary SQL script against the ephemeral test database using sqlcmd.
 * Includes precise error diagnostics and safeguards against retrying fixed configuration issues.
 */
function executeSqlInDb(state: E2eState, sql: string, maxAttempts = 5): void {
  const { server, user, password, trusted } = parseSqlConnectionString(state.connectionString);
  const args = ['-S', server, '-d', state.dbName, '-C', '-b'];
  if (trusted) {
    args.push('-E');
  } else if (user && password) {
    args.push('-U', user, '-P', password);
  }
  args.push('-Q', sql);

  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    const res = spawnSync('sqlcmd', args, {
      encoding: 'utf-8',
      timeout: 15_000,
    });

    if (res.error) {
      const errCode = (res.error as any).code;
      if (errCode === 'ENOENT') {
        throw new Error(
          `[Capture] CRITICAL: 'sqlcmd' executable was not found on PATH (ENOENT). Ensure sqlcmd is installed or use Runner e2e-sql commands.`
        );
      }
      if (errCode === 'EACCES') {
        throw new Error(
          `[Capture] CRITICAL: Permission denied executing 'sqlcmd' (EACCES).`
        );
      }
      if (errCode === 'ETIMEDOUT') {
        if (attempt === maxAttempts) {
          throw new Error(`[Capture] CRITICAL: 'sqlcmd' timed out after 15000ms.`);
        }
      }
    }

    if (res.status === 0) {
      if (res.stdout && res.stdout.trim().length > 0) {
        console.log(`[Capture SQL]\n${sanitizeTextForLog(res.stdout.trim(), state)}`);
      }
      return;
    }

    const sanitizedErr = sanitizeTextForLog(res.stderr || res.stdout || '', state);
    if (attempt < maxAttempts) {
      const waitBuffer = new Int32Array(new SharedArrayBuffer(4));
      Atomics.wait(waitBuffer, 0, 0, 600);
    } else {
      throw new Error(
        `[Capture] CRITICAL: SQL execution failed in ephemeral DB ${state.dbName} (exit ${res.status}, signal ${res.signal}):\n${sanitizedErr}`
      );
    }
  }
}

/**
 * Seeds an ADMIN user into the ephemeral test database using the Runner.
 * Avoids host-level sqlcmd dependencies and verifies exit status.
 */
function seedAdminUser(state: E2eState): void {
  runRunnerCommandOrThrow(['e2e-sql', 'seed-admin'], state);
  console.log(`[Capture] Seeded admin@demo.invalid with role ADMIN in ${state.dbName}`);
}

export async function runUiCapture(options?: {
  targetFolder?: string;
  port?: number;
  skipBrowserLaunchForTest?: boolean;
  stateFilePath?: string;
  existingState?: E2eState;
}): Promise<{ state: E2eState }> {
  const targetFolder = options?.targetFolder || process.argv[2] || 'ui-01a';
  const outDir = path.resolve(__dirname, '..', '..', 'docs', 'ui', 'screenshots', targetFolder);
  if (!fs.existsSync(outDir)) {
    fs.mkdirSync(outDir, { recursive: true });
  }

  const port = options?.port || 5195;
  const stateFilePath =
    options?.stateFilePath || path.resolve(__dirname, '..', '.tmp', 'capture-state.json');
  const state = options?.existingState || createInitialE2eState('cap_' + Date.now().toString(36), port);
  let browser: Browser | null = null;
  let setupCompleted = false;
  let originalError: unknown = null;

  try {
    console.log(`[Capture] Launching test web server on port ${port} with ephemeral DB: ${state.dbName}`);
    await runGlobalSetupLifecycle(state, { stateFilePath });
    setupCompleted = true;

    if (options?.skipBrowserLaunchForTest) {
      throw new Error('Simulated browser launch failure for lifecycle cleanup test');
    }

    // Seed ADMIN user into ephemeral DB using the runner
    seedAdminUser(state);

    browser = await chromium.launch();

    if (targetFolder === 'ui-p8') {
      // =========================================================================
      // UI-P8 Final Master Acceptance Gallery (Complete Zero-Bootstrap Journey)
      // =========================================================================
      console.log(`[Capture] Starting UI-P8 Final System-Wide Master Acceptance capture suite...`);
      runRunnerCommandOrThrow(['seed-verified-showcase'], state, {
        BGP_ALLOW_DEMO_SEED: 'true',
        BGP_DEMO_PASSWORD: state.demoPassword,
      });

      const tmpDir = path.resolve(__dirname, '..', '.tmp');
      if (!fs.existsSync(tmpDir)) {
        fs.mkdirSync(tmpDir, { recursive: true });
      }
      const qrCapturePath = path.join(tmpDir, 'uip8-final-qr.png');

      // 1. Manager publishes an ON_SALE showtime at now + 18m for Mê trong mê (Charade, 1963)
      const mgrCtx = await browser.newContext({ baseURL: state.baseUrl, viewport: { width: 1440, height: 900 } });
      const mgrPage = await mgrCtx.newPage();
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);

      const apiMovies = (await (await mgrPage.request.get('/api/movies')).json()) as any[];
      const charade = apiMovies.find((m) => m.provenance?.wikidataQid === 'Q496266') ?? apiMovies[0];

      await mgrPage.goto('/management/showtimes/create');
      await mgrPage.selectOption('select[name="MovieId"]', String(charade.movieId));
      const phong02Opt = mgrPage.locator('select[name="RoomId"] option', { hasText: 'Phòng 02' }).first();
      const room2Val = (await phong02Opt.getAttribute('value'))!;
      await mgrPage.selectOption('select[name="RoomId"]', room2Val);
      await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(new Date(Date.now() + 18 * 60_000)));
      await mgrPage.fill('input[name="BufferMinutes"]', '15');
      await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
      await mgrPage.fill('input[name="Prices[VIP]"]', '95000');
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      const scannableShowtimeId = Number(mgrPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);

      // 2. Customer End-to-End Journey (Home -> Catalog -> Detail -> Seat Map -> Checkout -> QR Boarding Pass -> Privacy Attribution)
      const custCtx = await browser.newContext({ baseURL: state.baseUrl, viewport: { width: 1440, height: 900 } });
      const custPage = await custCtx.newPage();
      await loginInBrowser(custPage, 'customer1@demo.invalid', state.demoPassword);

      await custPage.goto('/', { waitUntil: 'networkidle' });
      await custPage.waitForTimeout(180);
      await custPage.screenshot({ path: path.join(outDir, '01-final-home-cinema-desktop-1440.png'), fullPage: false });
      console.log(`Saved 01-final-home-cinema-desktop-1440.png`);

      await custPage.goto('/movies', { waitUntil: 'networkidle' });
      await custPage.waitForTimeout(160);
      await custPage.screenshot({ path: path.join(outDir, '02-final-catalog-verified-desktop-1440.png'), fullPage: false });
      console.log(`Saved 02-final-catalog-verified-desktop-1440.png`);

      await custPage.goto(`/movies/${charade.movieId}`, { waitUntil: 'networkidle' });
      await custPage.waitForTimeout(160);
      await custPage.screenshot({ path: path.join(outDir, '03-final-movie-detail-charade-1440.png'), fullPage: false });
      console.log(`Saved 03-final-movie-detail-charade-1440.png`);

      await custPage.goto(`/showtimes/${scannableShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await custPage.locator('.seat-box[data-seat-code="C5"]').click();
      await custPage.locator('.seat-box[data-seat-code="C6"]').click();
      await custPage.waitForTimeout(160);
      await custPage.screenshot({ path: path.join(outDir, '04-final-seat-map-selection-1440.png'), fullPage: false });
      console.log(`Saved 04-final-seat-map-selection-1440.png`);

      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnSubmit'),
      ]);
      const orderId = Number(custPage.url().match(/\/orders\/(\d+)$/)![1]);

      await custPage.goto(`/orders/${orderId}/checkout`, { waitUntil: 'networkidle' });
      await custPage.waitForTimeout(160);
      await custPage.screenshot({ path: path.join(outDir, '05-final-checkout-hold-countdown-1440.png'), fullPage: false });
      console.log(`Saved 05-final-checkout-hold-countdown-1440.png`);

      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnPaySuccess'),
      ]);
      await custPage.goto(`/orders/${orderId}/tickets`, { waitUntil: 'networkidle' });
      await custPage.waitForTimeout(180);
      await custPage.locator('[data-testid="ticket-qr-box"]').first().screenshot({ path: qrCapturePath });
      await custPage.screenshot({ path: path.join(outDir, '06-final-qr-boarding-passes-1440.png'), fullPage: true });
      console.log(`Saved 06-final-qr-boarding-passes-1440.png`);

      await custPage.goto('/privacy', { waitUntil: 'networkidle' });
      await custPage.waitForTimeout(160);
      await custPage.screenshot({ path: path.join(outDir, '10-final-privacy-and-attribution-1440.png'), fullPage: true });
      console.log(`Saved 10-final-privacy-and-attribution-1440.png`);
      await custCtx.close();

      // 3. Staff Optical QR Gate Scan (ACCEPTED)
      const staffCtx = await browser.newContext({ baseURL: state.baseUrl, viewport: { width: 1440, height: 900 } });
      const staffPage = await staffCtx.newPage();
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);
      await staffPage.goto(`/staff/check-in?showtimeId=${scannableShowtimeId}`, { waitUntil: 'networkidle' });
      await staffPage.locator('#qrFileInput').setInputFiles(qrCapturePath);
      await staffPage.locator('#scanStatusBadge', { hasText: 'HỢP LỆ' }).waitFor({ timeout: 15_000 });
      await staffPage.waitForTimeout(160);
      await staffPage.screenshot({ path: path.join(outDir, '07-final-staff-gate-scan-accepted-1440.png'), fullPage: false });
      console.log(`Saved 07-final-staff-gate-scan-accepted-1440.png`);
      await staffCtx.close();

      // 4. Manager Verified Movie Governance & Live Financial Reconciliation Dashboard
      await mgrPage.goto('/management/movies', { waitUntil: 'networkidle' });
      await mgrPage.waitForTimeout(160);
      await mgrPage.screenshot({ path: path.join(outDir, '08-final-mgmt-movies-governance-1440.png'), fullPage: false });
      console.log(`Saved 08-final-mgmt-movies-governance-1440.png`);

      await mgrPage.goto('/management/reports', { waitUntil: 'networkidle' });
      await mgrPage.waitForTimeout(160);
      await mgrPage.screenshot({ path: path.join(outDir, '09-final-mgmt-sales-reports-1440.png'), fullPage: true });
      console.log(`Saved 09-final-mgmt-sales-reports-1440.png`);
      await mgrCtx.close();

      if (fs.existsSync(qrCapturePath)) {
        fs.unlinkSync(qrCapturePath);
      }
    } else if (targetFolder === 'ui-p7') {
      // =========================================================================
      // UI-P7 High-Density Operations Workspace (STAFF / MANAGER / ADMIN)
      // & Controlled External Provenance Sync (Wikidata / IMDb / TMDB)
      // =========================================================================
      console.log(`[Capture] Starting UI-P7 Operations Console & External Provenance Sync capture suite...`);
      runRunnerCommandOrThrow(['seed-verified-showcase'], state, {
        BGP_ALLOW_DEMO_SEED: 'true',
        BGP_DEMO_PASSWORD: state.demoPassword,
      });

      const tmpDir = path.resolve(__dirname, '..', '.tmp');
      if (!fs.existsSync(tmpDir)) {
        fs.mkdirSync(tmpDir, { recursive: true });
      }
      const qrCapturePath = path.join(tmpDir, 'uip7-capture-qr.png');

      // 1. Manager publishes an ON_SALE showtime (now + 18m) for Charade (1963) so Staff can scan & Reports show live revenue
      const mgrDesktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const mgrPage = await mgrDesktopCtx.newPage();
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);

      const apiMovies = (await (await mgrPage.request.get('/api/movies')).json()) as any[];
      const charade = apiMovies.find((m) => m.provenance?.wikidataQid === 'Q496266') ?? apiMovies[0];

      await mgrPage.goto('/management/showtimes/create');
      await mgrPage.selectOption('select[name="MovieId"]', String(charade.movieId));
      const phong02Opt = mgrPage.locator('select[name="RoomId"] option', { hasText: 'Phòng 02' }).first();
      const room2Val = (await phong02Opt.getAttribute('value'))!;
      await mgrPage.selectOption('select[name="RoomId"]', room2Val);
      await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(new Date(Date.now() + 18 * 60_000)));
      await mgrPage.fill('input[name="BufferMinutes"]', '15');
      await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
      await mgrPage.fill('input[name="Prices[VIP]"]', '95000');
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      const scannableShowtimeId = Number(mgrPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);

      // 2. Customer buys 2 VIP seats + holds 1 seat in a second order so ShowtimeDetail & Reports have rich real states
      const custCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const custPage = await custCtx.newPage();
      await loginInBrowser(custPage, 'customer1@demo.invalid', state.demoPassword);

      await custPage.goto(`/showtimes/${scannableShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await custPage.locator('.seat-box[data-seat-code="C5"]').click();
      await custPage.locator('.seat-box[data-seat-code="C6"]').click();
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnSubmit'),
      ]);
      const paidOrderId = Number(custPage.url().match(/\/orders\/(\d+)$/)![1]);
      await custPage.goto(`/orders/${paidOrderId}/checkout`, { waitUntil: 'networkidle' });
      await Promise.all([
        custPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        custPage.click('#btnPaySuccess'),
      ]);
      await custPage.goto(`/orders/${paidOrderId}/tickets`, { waitUntil: 'networkidle' });
      await custPage.locator('[data-testid="ticket-qr-box"]').first().screenshot({ path: qrCapturePath });

      // Customer2 holds A4 so ShowtimeDetail matrix displays AVAILABLE + HELD + SOLD simultaneously
      const cust2Ctx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1280, height: 800 },
      });
      const cust2Page = await cust2Ctx.newPage();
      await loginInBrowser(cust2Page, 'customer2@demo.invalid', state.demoPassword);
      await cust2Page.goto(`/showtimes/${scannableShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await cust2Page.locator('.seat-box[data-seat-code="A4"]').click();
      await Promise.all([
        cust2Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust2Page.click('#btnSubmit'),
      ]);
      const heldOrderId = Number(cust2Page.url().match(/\/orders\/(\d+)$/)![1]);
      await custCtx.close();

      // 3. STAFF Gate Check-In Console (Desktop 1440 + Accepted State + Mobile 390 + Audit History)
      const staffDesktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const staffPage = await staffDesktopCtx.newPage();
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);

      await staffPage.goto(`/staff/check-in?showtimeId=${scannableShowtimeId}`, { waitUntil: 'networkidle' });
      await staffPage.waitForTimeout(160);
      await staffPage.screenshot({
        path: path.join(outDir, '01-staff-checkin-ready-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 01-staff-checkin-ready-desktop-1440.png`);

      await staffPage.locator('#qrFileInput').setInputFiles(qrCapturePath);
      await staffPage.locator('#scanStatusBadge', { hasText: 'HỢP LỆ' }).waitFor({ timeout: 15_000 });
      await staffPage.waitForTimeout(160);
      await staffPage.screenshot({
        path: path.join(outDir, '02-staff-checkin-accepted-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 02-staff-checkin-accepted-desktop-1440.png`);

      // Rescan once + manual invalid scan so Audit History shows ACCEPTED, ALREADY_USED & NOT_FOUND
      await staffPage.click('#btnRescanCurrent');
      await staffPage.locator('#scanStatusBadge', { hasText: 'ĐÃ SỬ DỤNG' }).waitFor({ timeout: 15_000 });
      await staffPage.click('#btnScanNext');
      await staffPage.fill('#manualQrToken', 'BGP_DEMO_INVALID_QR_AUDIT_SAMPLE_01');
      await staffPage.click('#btnManualSubmit');
      await staffPage.locator('#scanStatusBadge', { hasText: 'KHÔNG TỒN TẠI' }).waitFor({ timeout: 15_000 });

      await staffPage.goto('/staff/check-in/history', { waitUntil: 'networkidle' });
      await staffPage.waitForTimeout(160);
      await staffPage.screenshot({
        path: path.join(outDir, '03-staff-checkin-history-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 03-staff-checkin-history-desktop-1440.png`);
      await staffDesktopCtx.close();

      const staffMobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
        hasTouch: true,
        isMobile: true,
      });
      const staffMobilePage = await staffMobileCtx.newPage();
      await loginInBrowser(staffMobilePage, 'staff@demo.invalid', state.demoPassword);
      await staffMobilePage.goto(`/staff/check-in?showtimeId=${scannableShowtimeId}`, { waitUntil: 'networkidle' });
      await staffMobilePage.waitForTimeout(160);
      await staffMobilePage.screenshot({
        path: path.join(outDir, '04-staff-checkin-mobile-390.png'),
        fullPage: true,
      });
      console.log(`Saved 04-staff-checkin-mobile-390.png`);
      await staffMobileCtx.close();

      // 4. MANAGER Operations Console: Reports, External Lookup & Sync, MovieEdit Override Locks, ShowtimeDetail & RoomDetail
      await mgrPage.goto('/management/reports', { waitUntil: 'networkidle' });
      await mgrPage.waitForTimeout(160);
      await mgrPage.screenshot({
        path: path.join(outDir, '05-mgmt-reports-dashboard-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 05-mgmt-reports-dashboard-desktop-1440.png`);

      await mgrPage.goto('/management/movies', { waitUntil: 'networkidle' });
      await mgrPage.click('[data-testid="toggle-external-lookup-btn"]');
      await mgrPage.fill('[data-testid="external-lookup-input"]', 'e');
      await mgrPage.click('[data-sync-search-btn]');
      await mgrPage.locator('[data-testid="external-lookup-item"]').first().waitFor({ timeout: 10_000 });
      await mgrPage.waitForTimeout(180);
      await mgrPage.screenshot({
        path: path.join(outDir, '06-mgmt-movies-external-lookup-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 06-mgmt-movies-external-lookup-desktop-1440.png`);

      // Import Elephants Dream (tt0807840) and capture MovieDetail + MovieEdit with ManualOverrideMask locks
      await mgrPage.fill('[data-testid="external-lookup-input"]', 'tt0807840');
      await mgrPage.click('[data-sync-search-btn]');
      await mgrPage.locator('[data-testid="import-external-draft-btn"]').first().waitFor({ timeout: 10_000 });
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.locator('[data-testid="import-external-draft-btn"]').first().click(),
      ]);
      const importedId = Number(mgrPage.url().match(/\/management\/movies\/(\d+)$/)![1]);

      await mgrPage.goto(`/management/movies/${importedId}/edit`, { waitUntil: 'networkidle' });
      await mgrPage.fill('input[name="Title"]', 'Giấc Mơ Voi Máy (Elephants Dream)');
      await mgrPage.selectOption('select[name="AgeLabel"]', 'T13');
      await mgrPage.waitForTimeout(160);
      await mgrPage.screenshot({
        path: path.join(outDir, '07-mgmt-movie-edit-override-locks-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 07-mgmt-movie-edit-override-locks-desktop-1440.png`);

      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      await mgrPage.waitForLoadState('networkidle');
      await mgrPage.waitForTimeout(160);
      await mgrPage.screenshot({
        path: path.join(outDir, '08-mgmt-movie-detail-provenance-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 08-mgmt-movie-detail-provenance-desktop-1440.png`);

      await mgrPage.goto(`/management/showtimes/${scannableShowtimeId}`, { waitUntil: 'networkidle' });
      await mgrPage.waitForTimeout(160);
      await mgrPage.screenshot({
        path: path.join(outDir, '09-mgmt-showtime-detail-matrix-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 09-mgmt-showtime-detail-matrix-desktop-1440.png`);

      await mgrPage.goto(`/management/rooms/${room2Val}`, { waitUntil: 'networkidle' });
      await mgrPage.waitForTimeout(160);
      await mgrPage.screenshot({
        path: path.join(outDir, '10-mgmt-room-detail-matrix-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 10-mgmt-room-detail-matrix-desktop-1440.png`);
      await mgrDesktopCtx.close();

      // Mobile 390 Management Movies view
      const mgrMobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
        hasTouch: true,
        isMobile: true,
      });
      const mgrMobilePage = await mgrMobileCtx.newPage();
      await loginInBrowser(mgrMobilePage, 'manager@demo.invalid', state.demoPassword);
      await mgrMobilePage.goto('/management/movies', { waitUntil: 'networkidle' });
      await mgrMobilePage.waitForTimeout(160);
      await mgrMobilePage.screenshot({
        path: path.join(outDir, '11-mgmt-movies-mobile-390.png'),
        fullPage: true,
      });
      console.log(`Saved 11-mgmt-movies-mobile-390.png`);
      await mgrMobileCtx.close();

      // Clean up temporary HELD order before Runner verify teardown
      await cust2Page.goto(`/orders/${heldOrderId}`);
      await cust2Page.click(`form[action="/orders/${heldOrderId}/cancel"] button[type="submit"]`);
      await Promise.all([
        cust2Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust2Page.click('[data-testid="confirm-cancel-order-btn"]'),
      ]);
      await cust2Ctx.close();

      if (fs.existsSync(qrCapturePath)) {
        fs.unlinkSync(qrCapturePath);
      }
    } else if (targetFolder === 'ui-p6') {
      // =========================================================================
      // UI-P6 High-Contrast QR Boarding Passes, Order History & Cinema Identity
      // (/orders/{id}/tickets, /orders, /account/login, /account/profile)
      // =========================================================================
      console.log(`[Capture] Starting UI-P6 QR Boarding Passes, Orders & Account capture suite...`);
      runRunnerCommandOrThrow(['seed-verified-showcase'], state, {
        BGP_ALLOW_DEMO_SEED: 'true',
        BGP_DEMO_PASSWORD: state.demoPassword,
      });

      const tmpDir = path.resolve(__dirname, '..', '.tmp');
      if (!fs.existsSync(tmpDir)) {
        fs.mkdirSync(tmpDir, { recursive: true });
      }

      // Publish an ON_SALE showtime at now + 18 minutes in Phòng 02 so Staff check-in can scan immediately
      const mgrCtx = await browser.newContext({ baseURL: state.baseUrl, viewport: { width: 1440, height: 900 } });
      const mgrPage = await mgrCtx.newPage();
      mgrPage.on('dialog', (d) => d.accept());
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);

      const apiMovies = (await (await mgrPage.request.get('/api/movies')).json()) as any[];
      const charade = apiMovies.find((m) => m.provenance?.wikidataQid === 'Q496266') ?? apiMovies[0];
      const hisGirlFriday = apiMovies.find((m) => m.provenance?.wikidataQid === 'Q1066714') ?? charade;

      await mgrPage.goto('/management/showtimes/create');
      await mgrPage.selectOption('select[name="MovieId"]', String(charade.movieId));
      const phong02Opt = mgrPage.locator('select[name="RoomId"] option', { hasText: 'Phòng 02' }).first();
      const room2Val = (await phong02Opt.getAttribute('value'))!;
      await mgrPage.selectOption('select[name="RoomId"]', room2Val);
      await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(new Date(Date.now() + 18 * 60_000)));
      await mgrPage.fill('input[name="BufferMinutes"]', '15');
      await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
      await mgrPage.fill('input[name="Prices[VIP]"]', '95000');
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      const scannableShowtimeId = Number(mgrPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);

      // Also publish a second showtime for His Girl Friday (1940) in Phòng 03 (+3 days) for varied Order History posters
      await mgrPage.goto('/management/showtimes/create');
      await mgrPage.selectOption('select[name="MovieId"]', String(hisGirlFriday.movieId));
      const phong03Opt = mgrPage.locator('select[name="RoomId"] option', { hasText: 'Phòng 03' }).first();
      const room3Val = (await phong03Opt.getAttribute('value'))!;
      await mgrPage.selectOption('select[name="RoomId"]', room3Val);
      await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(new Date(Date.now() + 4 * 86400_000)));
      await mgrPage.fill('input[name="BufferMinutes"]', '15');
      await mgrPage.fill('input[name="Prices[STANDARD]"]', '70000');
      await mgrPage.fill('input[name="Prices[VIP]"]', '90000');
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      const secondShowtimeId = Number(mgrPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);
      await mgrCtx.close();

      // 08 & 09. Unauthenticated Login Desktop (1440) + Error State Mobile (390)
      const anonDesktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const anonDesktopPage = await anonDesktopCtx.newPage();
      await anonDesktopPage.goto('/account/login?returnUrl=%2Forders', { waitUntil: 'networkidle' });
      await anonDesktopPage.waitForTimeout(160);
      await anonDesktopPage.screenshot({
        path: path.join(outDir, '08-account-login-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 08-account-login-desktop-1440.png`);
      await anonDesktopCtx.close();

      const anonMobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
        hasTouch: true,
        isMobile: true,
      });
      const anonMobilePage = await anonMobileCtx.newPage();
      await anonMobilePage.goto('/account/login?returnUrl=%2Forders', { waitUntil: 'networkidle' });
      await anonMobilePage.click('[data-testid="demo-chip-customer1"]');
      await anonMobilePage.fill('input[name="Request.Password"]', 'WrongPassword_Demo123!');
      await Promise.all([
        anonMobilePage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        anonMobilePage.click('[data-testid="login-submit-btn"]'),
      ]);
      await anonMobilePage.waitForLoadState('networkidle');
      await anonMobilePage.waitForTimeout(160);
      await anonMobilePage.screenshot({
        path: path.join(outDir, '09-account-login-error-mobile-390.png'),
        fullPage: true,
      });
      console.log(`Saved 09-account-login-error-mobile-390.png`);
      await anonMobileCtx.close();

      // 07. True 0-Orders Empty State on /orders via fresh customer2@demo.invalid
      const emptyCustCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const emptyCustPage = await emptyCustCtx.newPage();
      await loginInBrowser(emptyCustPage, 'customer2@demo.invalid', state.demoPassword);
      await emptyCustPage.goto('/orders', { waitUntil: 'networkidle' });
      await emptyCustPage.waitForTimeout(150);
      await emptyCustPage.screenshot({
        path: path.join(outDir, '07-orders-history-empty-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 07-orders-history-empty-desktop-1440.png`);
      await emptyCustCtx.close();

      // Customer 1 Journey:
      // - Order #1 (Cancelled on secondShowtimeId)
      // - Order #2 (PAID on scannableShowtimeId: A2 Standard + C5 VIP -> 1 scanned USED + 1 ISSUED)
      // - Order #3 (Active Hold PENDING_PAYMENT on secondShowtimeId)
      const desktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const desktopPage = await desktopCtx.newPage();
      await loginInBrowser(desktopPage, 'customer1@demo.invalid', state.demoPassword);

      // Order #1: CANCELLED
      await desktopPage.goto(`/showtimes/${secondShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await desktopPage.locator('.seat-box[data-seat-code="A2"]').click();
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('#btnSubmit'),
      ]);
      await desktopPage.click('#btnCancel');
      await desktopPage.locator('[data-testid="cancel-order-dialog"]').waitFor({ state: 'visible' });
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('[data-testid="confirm-cancel-order-btn"]'),
      ]);

      // Order #2: PAID (A2 Standard + C5 VIP) -> Scan 1st ticket via Staff Check-In so page shows USED + ISSUED
      await desktopPage.goto(`/showtimes/${scannableShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await desktopPage.locator('.seat-box[data-seat-code="A2"]').click();
      await desktopPage.locator('.seat-box[data-seat-code="C5"]').click();
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('#btnSubmit'),
      ]);
      const paidOrderId = Number(desktopPage.url().match(/\/orders\/(\d+)$/)![1]);
      await desktopPage.goto(`/orders/${paidOrderId}/checkout`, { waitUntil: 'networkidle' });
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('#btnPaySuccess'),
      ]);
      await desktopPage.waitForLoadState('networkidle');

      // Render QR box screenshot and scan via Staff Check-In
      const qrCapturePath = path.join(tmpDir, `capture-uip6-qr-${paidOrderId}.png`);
      await desktopPage.locator('[data-testid="ticket-qr-box"]').first().screenshot({ path: qrCapturePath });

      const staffCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const staffPage = await staffCtx.newPage();
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);
      await staffPage.goto(`/staff/check-in?showtimeId=${scannableShowtimeId}`, { waitUntil: 'networkidle' });
      await staffPage.locator('#qrFileInput').setInputFiles(qrCapturePath);
      await staffPage.locator('#scanStatusBadge', { hasText: 'HỢP LỆ' }).waitFor({ state: 'visible', timeout: 15_000 });
      await staffCtx.close();
      if (fs.existsSync(qrCapturePath)) {
        fs.unlinkSync(qrCapturePath);
      }

      // 01. Reload /orders/{id}/tickets Desktop (1440) showing 1 USED + 1 ISSUED boarding pass
      await desktopPage.goto(`/orders/${paidOrderId}/tickets`, { waitUntil: 'networkidle' });
      await desktopPage.waitForTimeout(180);
      await desktopPage.screenshot({
        path: path.join(outDir, '01-tickets-qr-issued-and-used-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 01-tickets-qr-issued-and-used-desktop-1440.png`);

      // 03. Emulated Print Mode (@media print) on /orders/{id}/tickets
      await desktopPage.emulateMedia({ media: 'print' });
      await desktopPage.waitForTimeout(160);
      await desktopPage.screenshot({
        path: path.join(outDir, '03-tickets-qr-print-mode-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 03-tickets-qr-print-mode-1440.png`);
      await desktopPage.emulateMedia({ media: 'screen' });

      // Order #3: Active Hold (PENDING_PAYMENT) on secondShowtimeId so /orders displays all 3 states
      await desktopPage.goto(`/showtimes/${secondShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await desktopPage.locator('.seat-box[data-seat-code="C3"]').click();
      await desktopPage.locator('.seat-box[data-seat-code="C4"]').click();
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('#btnSubmit'),
      ]);

      // 04. Populated Order History (/orders) Desktop 1440
      await desktopPage.goto('/orders', { waitUntil: 'networkidle' });
      await desktopPage.waitForTimeout(180);
      await desktopPage.screenshot({
        path: path.join(outDir, '04-orders-history-populated-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 04-orders-history-populated-desktop-1440.png`);

      // 05. Filtered Order History (/orders?status=paid) Desktop 1440
      await desktopPage.goto('/orders?status=paid', { waitUntil: 'networkidle' });
      await desktopPage.waitForTimeout(160);
      await desktopPage.screenshot({
        path: path.join(outDir, '05-orders-history-filtered-paid-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 05-orders-history-filtered-paid-desktop-1440.png`);

      // 10. Customer Profile (/account/profile) Desktop 1440
      await desktopPage.goto('/account/profile', { waitUntil: 'networkidle' });
      await desktopPage.waitForTimeout(160);
      await desktopPage.screenshot({
        path: path.join(outDir, '10-account-profile-customer-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 10-account-profile-customer-desktop-1440.png`);
      await desktopCtx.close();

      // 02 & 06. Mobile (390x844) Tickets & Order History
      const mobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
        hasTouch: true,
        isMobile: true,
      });
      const mobilePage = await mobileCtx.newPage();
      await loginInBrowser(mobilePage, 'customer1@demo.invalid', state.demoPassword);

      await mobilePage.goto(`/orders/${paidOrderId}/tickets`, { waitUntil: 'networkidle' });
      await mobilePage.waitForTimeout(180);
      await mobilePage.screenshot({
        path: path.join(outDir, '02-tickets-qr-mobile-390.png'),
        fullPage: true,
      });
      console.log(`Saved 02-tickets-qr-mobile-390.png`);

      await mobilePage.goto('/orders', { waitUntil: 'networkidle' });
      await mobilePage.waitForTimeout(180);
      await mobilePage.screenshot({
        path: path.join(outDir, '06-orders-history-mobile-390.png'),
        fullPage: true,
      });
      console.log(`Saved 06-orders-history-mobile-390.png`);
      await mobileCtx.close();
    } else if (targetFolder === 'ui-p5') {
      // =========================================================================
      // UI-P5 Transparent Seat Hold & Academic Mock Checkout Captures
      // (/orders/{id} & /orders/{id}/checkout — 100% Real Verified Film Metadata)
      // =========================================================================
      console.log(`[Capture] Starting UI-P5 Transparent Seat Hold & Checkout capture suite...`);
      runRunnerCommandOrThrow(['seed-verified-showcase'], state, {
        BGP_ALLOW_DEMO_SEED: 'true',
        BGP_DEMO_PASSWORD: state.demoPassword,
      });

      const mgrCtx = await browser.newContext({ baseURL: state.baseUrl, viewport: { width: 1440, height: 900 } });
      const mgrPage = await mgrCtx.newPage();
      mgrPage.on('dialog', (d) => d.accept());
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);

      const apiMovies = (await (await mgrPage.request.get('/api/movies')).json()) as any[];
      const charade = apiMovies.find((m) => m.provenance?.wikidataQid === 'Q496266') ?? apiMovies[0];

      await mgrPage.goto('/management/showtimes/create');
      await mgrPage.selectOption('select[name="MovieId"]', String(charade.movieId));
      const phong02Opt = mgrPage.locator('select[name="RoomId"] option', { hasText: 'Phòng 02' }).first();
      const roomVal = (await phong02Opt.getAttribute('value'))!;
      await mgrPage.selectOption('select[name="RoomId"]', roomVal);
      await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(new Date(Date.now() + 5 * 86400_000)));
      await mgrPage.fill('input[name="BufferMinutes"]', '15');
      await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
      await mgrPage.fill('input[name="Prices[VIP]"]', '95000');
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      const showtimeId = Number(mgrPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);
      await mgrCtx.close();

      const desktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const desktopPage = await desktopCtx.newPage();
      await loginInBrowser(desktopPage, 'customer1@demo.invalid', state.demoPassword);

      // Reserve Order #1 (A3 Standard + C5, C6 VIP) -> Active Order Details, Active Checkout, Dialogs, and FAILED state
      await desktopPage.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await desktopPage.locator('.seat-box[data-seat-code="A3"]').click();
      await desktopPage.locator('.seat-box[data-seat-code="C5"]').click();
      await desktopPage.locator('.seat-box[data-seat-code="C6"]').click();
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('#btnSubmit'),
      ]);
      const firstOrderId = Number(desktopPage.url().match(/\/orders\/(\d+)$/)![1]);
      await desktopPage.waitForLoadState('networkidle');
      await desktopPage.waitForTimeout(160);

      // 01. Active Hold Order Details (PENDING_PAYMENT) Desktop 1440
      await desktopPage.screenshot({
        path: path.join(outDir, '01-order-details-active-hold-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 01-order-details-active-hold-desktop-1440.png`);

      // 02. Active Hold Checkout (QR Boarding Pass Preview + Honest MIS Simulator) Desktop 1440
      await desktopPage.goto(`/orders/${firstOrderId}/checkout`, { waitUntil: 'networkidle' });
      await desktopPage.waitForTimeout(160);
      await desktopPage.screenshot({
        path: path.join(outDir, '02-checkout-active-hold-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 02-checkout-active-hold-desktop-1440.png`);

      // 03. Accessible <dialog> Confirmation for Cancelling Hold (then dismissed)
      await desktopPage.click('#btnCancelCheckout');
      await desktopPage.locator('[data-testid="cancel-order-dialog"]').waitFor({ state: 'visible' });
      await desktopPage.waitForTimeout(140);
      await desktopPage.screenshot({
        path: path.join(outDir, '03-checkout-cancel-dialog-desktop-1440.png'),
        fullPage: false,
      });
      console.log(`Saved 03-checkout-cancel-dialog-desktop-1440.png`);
      await desktopPage.click('[data-testid="dismiss-cancel-dialog-btn"]');
      await desktopPage.waitForTimeout(120);

      // 04. Accessible <dialog> Confirmation for Simulated FAILED Payment (then confirmed)
      await desktopPage.click('#btnPayFailed');
      await desktopPage.locator('[data-testid="fail-payment-dialog"]').waitFor({ state: 'visible' });
      await desktopPage.waitForTimeout(140);
      await desktopPage.screenshot({
        path: path.join(outDir, '04-checkout-fail-payment-dialog-desktop-1440.png'),
        fullPage: false,
      });
      console.log(`Saved 04-checkout-fail-payment-dialog-desktop-1440.png`);

      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('[data-testid="confirm-fail-payment-btn"]'),
      ]);
      await desktopPage.waitForLoadState('networkidle');
      await desktopPage.waitForTimeout(150);

      // 05. Order Details in FAILED State
      await desktopPage.screenshot({
        path: path.join(outDir, '05-order-details-failed-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 05-order-details-failed-desktop-1440.png`);

      // Reserve Order #2 (B5) -> Live 00:00 Hold Expiry Auto-Lockout -> EXPIRED -> NEEDS_REVIEW
      await desktopPage.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await desktopPage.locator('.seat-box[data-seat-code="B5"]').click();
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('#btnSubmit'),
      ]);
      const expiringOrderId = Number(desktopPage.url().match(/\/orders\/(\d+)$/)![1]);

      runRunnerCommandOrThrow(['e2e-sql', 'set-order-remaining-seconds'], state, {
        BGP_E2E_ORDER_ID: String(expiringOrderId),
        BGP_E2E_REMAINING_SECONDS: '2',
      });

      await desktopPage.goto(`/orders/${expiringOrderId}/checkout`, { waitUntil: 'networkidle' });
      await desktopPage.locator('[data-testid="hold-expired-inline-panel"]').waitFor({
        state: 'visible',
        timeout: 8000,
      });
      await desktopPage.waitForTimeout(160);

      // 06. Checkout Live 00:00 (Hết hạn) Lockout + Inline Reselect Panel
      await desktopPage.screenshot({
        path: path.join(outDir, '06-checkout-hold-expired-00-00-lockout-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 06-checkout-hold-expired-00-00-lockout-1440.png`);

      // Run expire command so Order #2 releases held seat B5 cleanly into EXPIRED
      runRunnerCommandOrThrow(['expire'], state);

      // 07. Order Details in EXPIRED State
      await desktopPage.goto(`/orders/${expiringOrderId}`, { waitUntil: 'networkidle' });
      await desktopPage.waitForTimeout(150);
      await desktopPage.screenshot({
        path: path.join(outDir, '07-order-details-expired-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 07-order-details-expired-desktop-1440.png`);

      // 08. Order Details in NEEDS_REVIEW State
      runRunnerCommandOrThrow(['e2e-sql', 'set-order-needs-review'], state, {
        BGP_E2E_ORDER_ID: String(expiringOrderId),
      });
      await desktopPage.goto(`/orders/${expiringOrderId}`, { waitUntil: 'networkidle' });
      await desktopPage.waitForTimeout(150);
      await desktopPage.screenshot({
        path: path.join(outDir, '08-order-details-needs-review-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 08-order-details-needs-review-desktop-1440.png`);

      // Reserve Order #3 (C5 + C6) -> Pay SUCCESS -> Capture PAID Order Details
      await desktopPage.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await desktopPage.locator('.seat-box[data-seat-code="C5"]').click();
      await desktopPage.locator('.seat-box[data-seat-code="C6"]').click();
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('#btnSubmit'),
      ]);
      const paidOrderId = Number(desktopPage.url().match(/\/orders\/(\d+)$/)![1]);
      await desktopPage.goto(`/orders/${paidOrderId}/checkout`, { waitUntil: 'networkidle' });
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('#btnPaySuccess'),
      ]);

      // 09. Order Details in PAID State
      await desktopPage.goto(`/orders/${paidOrderId}`, { waitUntil: 'networkidle' });
      await desktopPage.waitForTimeout(150);
      await desktopPage.screenshot({
        path: path.join(outDir, '09-order-details-paid-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 09-order-details-paid-desktop-1440.png`);

      // Reserve Order #4 (D5 + D6) -> Mobile 390 + Narrow Mobile 320 + Cancel -> CANCELLED Order Details
      await desktopPage.goto(`/showtimes/${showtimeId}/seats`, { waitUntil: 'networkidle' });
      await desktopPage.locator('.seat-box[data-seat-code="D5"]').click();
      await desktopPage.locator('.seat-box[data-seat-code="D6"]').click();
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('#btnSubmit'),
      ]);
      const mobileOrderId = Number(desktopPage.url().match(/\/orders\/(\d+)$/)![1]);

      // 10. Mobile (390x844) Checkout Full Page
      const mobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
        hasTouch: true,
        isMobile: true,
      });
      const mobilePage = await mobileCtx.newPage();
      await loginInBrowser(mobilePage, 'customer1@demo.invalid', state.demoPassword);
      await mobilePage.goto(`/orders/${mobileOrderId}/checkout`, { waitUntil: 'networkidle' });
      await mobilePage.waitForTimeout(160);
      await mobilePage.screenshot({
        path: path.join(outDir, '10-checkout-mobile-390.png'),
        fullPage: true,
      });
      console.log(`Saved 10-checkout-mobile-390.png`);
      await mobileCtx.close();

      // 11. Narrow Mobile (320x568) Checkout (0px Horizontal Overflow Proof)
      const narrowCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 320, height: 568 },
        hasTouch: true,
        isMobile: true,
      });
      const narrowPage = await narrowCtx.newPage();
      await loginInBrowser(narrowPage, 'customer1@demo.invalid', state.demoPassword);
      await narrowPage.goto(`/orders/${mobileOrderId}/checkout`, { waitUntil: 'networkidle' });
      await narrowPage.waitForTimeout(160);
      await narrowPage.screenshot({
        path: path.join(outDir, '11-checkout-narrow-mobile-320.png'),
        fullPage: false,
      });
      console.log(`Saved 11-checkout-narrow-mobile-320.png`);
      await narrowCtx.close();

      // Cancel Order #4 via accessible <dialog> so zero HELD seats remain before Runner verify teardown
      await desktopPage.goto(`/orders/${mobileOrderId}`, { waitUntil: 'networkidle' });
      await desktopPage.click('#btnCancel');
      await desktopPage.locator('[data-testid="cancel-order-dialog"]').waitFor({ state: 'visible' });
      await Promise.all([
        desktopPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        desktopPage.click('[data-testid="confirm-cancel-order-btn"]'),
      ]);
      await desktopPage.waitForLoadState('networkidle');
      await desktopPage.waitForTimeout(150);

      // 12. Order Details in CANCELLED State
      await desktopPage.screenshot({
        path: path.join(outDir, '12-order-details-cancelled-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 12-order-details-cancelled-desktop-1440.png`);

      await desktopCtx.close();
    } else if (targetFolder === 'ui-p4') {
      // =========================================================================
      // UI-P4 Multi-Cue Auditorium Seat Map & 2D Keyboard Accessibility Captures
      // =========================================================================
      console.log(`[Capture] Starting UI-P4 Multi-Cue Seat Map capture suite...`);
      runRunnerCommandOrThrow(['seed-verified-showcase'], state, {
        BGP_ALLOW_DEMO_SEED: 'true',
        BGP_DEMO_PASSWORD: state.demoPassword,
      });

      // Setup active 8-col PHONG_02 showtime (with 1 SOLD + 1 HELD seat) and 1 CLOSED showtime for Mê trong mê (Charade, 1963)
      const mgrCtx = await browser.newContext({ baseURL: state.baseUrl, viewport: { width: 1440, height: 900 } });
      const cust2Ctx = await browser.newContext({ baseURL: state.baseUrl, viewport: { width: 1440, height: 900 } });
      const mgrPage = await mgrCtx.newPage();
      const cust2Page = await cust2Ctx.newPage();
      mgrPage.on('dialog', (d) => d.accept());
      cust2Page.on('dialog', (d) => d.accept());

      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);
      await loginInBrowser(cust2Page, 'customer2@demo.invalid', state.demoPassword);

      const apiMovies = (await (await mgrPage.request.get('/api/movies')).json()) as any[];
      const charade = apiMovies.find((m) => m.provenance?.wikidataQid === 'Q496266') ?? apiMovies[0];

      await mgrPage.goto('/management/showtimes/create');
      await mgrPage.selectOption('select[name="MovieId"]', String(charade.movieId));
      const phong02Opt = mgrPage.locator('select[name="RoomId"] option', { hasText: 'Phòng 02' }).first();
      const roomVal = (await phong02Opt.getAttribute('value'))!;
      await mgrPage.selectOption('select[name="RoomId"]', roomVal);
      await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(new Date(Date.now() + 5 * 86400_000)));
      await mgrPage.fill('input[name="BufferMinutes"]', '15');
      await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
      await mgrPage.fill('input[name="Prices[VIP]"]', '95000');
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      const activeShowtimeId = Number(mgrPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);

      // Create 1 SOLD seat (B4) and 1 HELD seat (C4) on activeShowtimeId
      await cust2Page.goto(`/showtimes/${activeShowtimeId}/seats`);
      await cust2Page.locator('.seat-box[data-seat-code="B4"]').click();
      await Promise.all([
        cust2Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust2Page.click('#btnSubmit'),
      ]);
      const soldOrderId = Number(cust2Page.url().match(/\/orders\/(\d+)$/)![1]);
      await cust2Page.goto(`/orders/${soldOrderId}/checkout`);
      await Promise.all([
        cust2Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust2Page.click('#btnPaySuccess'),
      ]);

      await cust2Page.goto(`/showtimes/${activeShowtimeId}/seats`);
      await cust2Page.locator('.seat-box[data-seat-code="C4"]').click();
      await Promise.all([
        cust2Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust2Page.click('#btnSubmit'),
      ]);
      const heldOrderId = Number(cust2Page.url().match(/\/orders\/(\d+)$/)![1]);

      // Create 1 CLOSED showtime (+6 days)
      await mgrPage.goto('/management/showtimes/create');
      await mgrPage.selectOption('select[name="MovieId"]', String(charade.movieId));
      await mgrPage.selectOption('select[name="RoomId"]', roomVal);
      await mgrPage.fill('input[name="StartTimeVn"]', formatVnDateTimeLocal(new Date(Date.now() + 6 * 86400_000)));
      await mgrPage.fill('input[name="BufferMinutes"]', '15');
      await mgrPage.fill('input[name="Prices[STANDARD]"]', '75000');
      await mgrPage.fill('input[name="Prices[VIP]"]', '95000');
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('main button[type="submit"]'),
      ]);
      const closedShowtimeId = Number(mgrPage.url().match(/\/management\/showtimes\/(\d+)$/)![1]);
      await mgrPage.click(`form[action*="/management/showtimes/${closedShowtimeId}/close"] button[type="submit"]`);
      await mgrPage.locator('[data-testid="close-showtime-dialog"]').waitFor({ state: 'visible' });
      await Promise.all([
        mgrPage.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        mgrPage.click('[data-testid="confirm-close-showtime-btn"]'),
      ]);
      await mgrCtx.close();

      // 1. Desktop (1440x900) Customer: Standard (A3) + VIP (C5, C6) selected alongside SOLD (B4) & HELD (C4)
      const desktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const desktopPage = await desktopCtx.newPage();
      await loginInBrowser(desktopPage, 'customer1@demo.invalid', state.demoPassword);
      await desktopPage.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });

      await desktopPage.locator('.seat-box[data-seat-code="A3"]').click();
      await desktopPage.locator('.seat-box[data-seat-code="C5"]').click();
      await desktopPage.locator('.seat-box[data-seat-code="C6"]').click();
      await desktopPage.waitForTimeout(160);
      await desktopPage.screenshot({
        path: path.join(outDir, '01-seat-map-desktop-1440-selected-std-vip.png'),
        fullPage: true,
      });
      console.log(`Saved 01-seat-map-desktop-1440-selected-std-vip.png`);

      // 2. Desktop (1440x900) Keyboard Focus Ring via Roving Tabindex (ArrowRight)
      await desktopPage.locator('.seat-box[data-seat-code="C6"]').focus();
      await desktopPage.keyboard.press('ArrowRight');
      await desktopPage.waitForTimeout(140);
      await desktopPage.screenshot({
        path: path.join(outDir, '02-seat-map-desktop-1440-keyboard-focus-ring.png'),
        fullPage: false,
      });
      console.log(`Saved 02-seat-map-desktop-1440-keyboard-focus-ring.png`);

      // 3. Desktop (1440x900) Full Grayscale(1) Accessibility Proof (all 5 states distinguishable without hue)
      await desktopPage.evaluate(() => {
        document.documentElement.style.filter = 'grayscale(1)';
      });
      await desktopPage.waitForTimeout(120);
      await desktopPage.screenshot({
        path: path.join(outDir, '03-seat-map-desktop-1440-grayscale-a11y.png'),
        fullPage: false,
      });
      console.log(`Saved 03-seat-map-desktop-1440-grayscale-a11y.png`);
      await desktopPage.evaluate(() => {
        document.documentElement.style.filter = '';
      });

      // 4. Desktop (1440x900) Max-8 Seat Limit Inline Live Banner (0 window.alert)
      for (const extraCode of ['A4', 'A5', 'A6', 'D5', 'D6', 'D7']) {
        await desktopPage.locator(`.seat-box[data-seat-code="${extraCode}"]`).click();
      }
      await desktopPage.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' as ScrollBehavior }));
      await desktopPage.waitForTimeout(160);
      await desktopPage.screenshot({
        path: path.join(outDir, '04-seat-map-desktop-1440-max-8-inline-banner.png'),
        fullPage: false,
      });
      console.log(`Saved 04-seat-map-desktop-1440-max-8-inline-banner.png`);

      // 5. Desktop (1440x900) Closed Showtime State
      await desktopPage.goto(`/showtimes/${closedShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '05-seat-map-desktop-1440-closed-showtime.png'),
        fullPage: false,
      });
      console.log(`Saved 05-seat-map-desktop-1440-closed-showtime.png`);
      await desktopCtx.close();

      // 6. Desktop (1440x900) Operational Role Lock (STAFF view-only inspection mode)
      const staffCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const staffPage = await staffCtx.newPage();
      await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);
      await staffPage.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await staffPage.screenshot({
        path: path.join(outDir, '06-seat-map-desktop-1440-operational-lock-staff.png'),
        fullPage: false,
      });
      console.log(`Saved 06-seat-map-desktop-1440-operational-lock-staff.png`);
      await staffCtx.close();

      // 7 & 8. Mobile (390x844): 100% Scroll Shadow + Sticky Bottom Dock, then Fit-to-Screen Zoom Mode
      const mobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
        hasTouch: true,
        isMobile: true,
      });
      const mobilePage = await mobileCtx.newPage();
      await loginInBrowser(mobilePage, 'customer1@demo.invalid', state.demoPassword);
      await mobilePage.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await mobilePage.locator('.seat-box[data-seat-code="C5"]').click();
      await mobilePage.locator('.seat-box[data-seat-code="C6"]').click();
      await mobilePage.evaluate(() => window.scrollTo({ top: 140, behavior: 'instant' as ScrollBehavior }));
      await mobilePage.waitForTimeout(200);
      await mobilePage.screenshot({
        path: path.join(outDir, '07-seat-map-mobile-390-scroll-and-dock.png'),
        fullPage: false,
      });
      console.log(`Saved 07-seat-map-mobile-390-scroll-and-dock.png`);

      await mobilePage.click('[data-testid="seat-zoom-toggle"]');
      await mobilePage.waitForTimeout(200);
      await mobilePage.screenshot({
        path: path.join(outDir, '08-seat-map-mobile-390-fit-zoom.png'),
        fullPage: false,
      });
      console.log(`Saved 08-seat-map-mobile-390-fit-zoom.png`);
      await mobileCtx.close();

      // 9. Narrow Mobile (320x568): Fit-to-Screen Zoom + Strict 0px Horizontal Overflow
      const narrowCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 320, height: 568 },
        hasTouch: true,
        isMobile: true,
      });
      const narrowPage = await narrowCtx.newPage();
      await narrowPage.goto(`/showtimes/${activeShowtimeId}/seats`, { waitUntil: 'networkidle' });
      await narrowPage.click('[data-testid="seat-zoom-toggle"]');
      await narrowPage.waitForTimeout(180);
      await narrowPage.screenshot({
        path: path.join(outDir, '09-seat-map-narrow-mobile-320-fit-zoom.png'),
        fullPage: false,
      });
      console.log(`Saved 09-seat-map-narrow-mobile-320-fit-zoom.png`);
      await narrowCtx.close();

      // Cleanup temporary HELD order before Runner verify teardown
      await cust2Page.goto(`/orders/${heldOrderId}`);
      await cust2Page.click(`form[action="/orders/${heldOrderId}/cancel"] button[type="submit"]`);
      await Promise.all([
        cust2Page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        cust2Page.click('[data-testid="confirm-cancel-order-btn"]'),
      ]);
      await cust2Ctx.close();
    } else if (targetFolder === 'ui-p3') {
      // =========================================================================
      // UI-P3 Netflix-Cinema Discovery Experience Captures (100% Real Verified Films)
      // =========================================================================
      console.log(`[Capture] Starting UI-P3 Netflix-Cinema Discovery capture suite (100% real verified films)...`);
      runRunnerCommandOrThrow(['seed-verified-showcase'], state, {
        BGP_ALLOW_DEMO_SEED: 'true',
        BGP_DEMO_PASSWORD: state.demoPassword,
      });

      const stubTrailerHtml = `<!doctype html><html lang="vi"><head><meta charset="utf-8"></head><body style="margin:0;height:100vh;background:radial-gradient(circle at center,#1c1c26 0%,#07070a 100%);color:#f5f5f7;font-family:'Be Vietnam Pro',system-ui,sans-serif;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;text-align:center;">
        <div style="width:68px;height:68px;border-radius:50%;background:#E50914;display:flex;align-items:center;justify-content:center;box-shadow:0 8px 28px rgba(229,9,20,0.48);">
          <svg width="26" height="26" viewBox="0 0 24 24" fill="#fff"><polygon points="6 4 20 12 6 20 6 4"/></svg>
        </div>
        <div style="font-size:18px;font-weight:800;">Official Theatrical Trailer — Charade (1963)</div>
        <div style="font-size:12.5px;color:#A1A1AA;">youtube-nocookie.com/embed/BJ_njww9SOQ • Click-to-Load Privacy-Enhanced Player</div>
      </body></html>`;

      // 1. Desktop (1440x900): Homepage, Hover Popover, Catalogue, Empty Filter, Pending Placeholder, Movie Details & Trailer Modal
      const desktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const desktopPage = await desktopCtx.newPage();
      await desktopPage.route('https://www.youtube-nocookie.com/embed/**', (route) =>
        route.fulfill({ status: 200, contentType: 'text/html; charset=utf-8', body: stubTrailerHtml })
      );

      await desktopPage.goto('/', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '01-home-hero-and-rails-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 01-home-hero-and-rails-desktop-1440.png`);

      // Open Quick-Preview Popover on Home Rail
      const previewTrigger = desktopPage.locator('[data-testid="wide-card-preview-btn"]').first();
      await previewTrigger.scrollIntoViewIfNeeded();
      await previewTrigger.click();
      await desktopPage.waitForTimeout(260);
      await desktopPage.screenshot({
        path: path.join(outDir, '02-home-quick-preview-popover-desktop-1440.png'),
        fullPage: false,
      });
      console.log(`Saved 02-home-quick-preview-popover-desktop-1440.png`);

      // Catalogue (/movies) — Full 11 verified films
      await desktopPage.goto('/movies', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '03-catalog-full-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 03-catalog-full-desktop-1440.png`);

      // Catalogue (/movies?filter=no-showtimes) — Honest neutral placeholder for PENDING_TMDB_API_KEY (Mai T18, Đào, phở và piano T13)
      await desktopPage.goto('/movies?filter=no-showtimes', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '04-catalog-no-showtimes-honest-placeholder-1440.png'),
        fullPage: false,
      });
      console.log(`Saved 04-catalog-no-showtimes-honest-placeholder-1440.png`);

      // Catalogue Empty Filter State (/movies?q=KhongCoPhimNay)
      await desktopPage.goto('/movies?q=KhongTimThayTacPhamNay', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '05-catalog-empty-filter-desktop-1440.png'),
        fullPage: false,
      });
      console.log(`Saved 05-catalog-empty-filter-desktop-1440.png`);

      // Movie Details (/movies/{id}) — Mê trong mê (Charade, 1963) with Cast, UTC+7 Date Tabs (+1 ngày) & Provenance Dossier
      await desktopPage.goto('/movies?q=Charade', { waitUntil: 'networkidle' });
      const charadeHref = (await desktopPage.locator('[data-testid="catalog-movie-card"]').first().getAttribute('href'))!;
      await desktopPage.goto(charadeHref, { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '06-movie-detail-charade-desktop-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 06-movie-detail-charade-desktop-1440.png`);

      // Official YouTube Trailer Modal (<dialog>) open on Movie Details
      await desktopPage.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' as ScrollBehavior }));
      await desktopPage.click('[data-testid="detail-trailer-btn"]');
      await desktopPage.waitForTimeout(260);
      await desktopPage.screenshot({
        path: path.join(outDir, '07-movie-detail-trailer-dialog-1440.png'),
        fullPage: false,
      });
      console.log(`Saved 07-movie-detail-trailer-dialog-1440.png`);

      // Movie Details — Zero-Showtime Vietnamese Film ("Mai", T18 VN) with Suggested On-Sale Movies
      await desktopPage.goto('/movies?q=Tr%E1%BA%A5n+Th%C3%A0nh', { waitUntil: 'networkidle' });
      const maiHref = (await desktopPage.locator('[data-testid="catalog-movie-card"]', { hasText: 'Mai' }).getAttribute('href'))!;
      await desktopPage.goto(maiHref, { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '08-movie-detail-mai-zero-showtimes-1440.png'),
        fullPage: true,
      });
      console.log(`Saved 08-movie-detail-mai-zero-showtimes-1440.png`);
      await desktopCtx.close();

      // 2. Tablet (768x1024): Homepage & Movie Details
      const tabletCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 768, height: 1024 },
      });
      const tabletPage = await tabletCtx.newPage();
      await tabletPage.goto('/', { waitUntil: 'networkidle' });
      await tabletPage.screenshot({
        path: path.join(outDir, '09-home-tablet-768.png'),
        fullPage: false,
      });
      console.log(`Saved 09-home-tablet-768.png`);
      await tabletCtx.close();

      // 3. Mobile (390x844 & 320x568): Homepage, Catalogue & Movie Details
      const mobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
        hasTouch: true,
        isMobile: true,
      });
      const mobilePage = await mobileCtx.newPage();
      await mobilePage.goto('/', { waitUntil: 'networkidle' });
      await mobilePage.screenshot({
        path: path.join(outDir, '10-home-mobile-390.png'),
        fullPage: false,
      });
      console.log(`Saved 10-home-mobile-390.png`);

      await mobilePage.goto('/movies', { waitUntil: 'networkidle' });
      await mobilePage.screenshot({
        path: path.join(outDir, '11-catalog-mobile-390.png'),
        fullPage: false,
      });
      console.log(`Saved 11-catalog-mobile-390.png`);

      await mobilePage.goto(charadeHref, { waitUntil: 'networkidle' });
      await mobilePage.screenshot({
        path: path.join(outDir, '12-movie-detail-charade-mobile-390.png'),
        fullPage: true,
      });
      console.log(`Saved 12-movie-detail-charade-mobile-390.png`);
      await mobileCtx.close();

      const narrowCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 320, height: 568 },
        hasTouch: true,
        isMobile: true,
      });
      const narrowPage = await narrowCtx.newPage();
      await narrowPage.goto('/', { waitUntil: 'networkidle' });
      await narrowPage.screenshot({
        path: path.join(outDir, '13-home-narrow-mobile-320.png'),
        fullPage: false,
      });
      console.log(`Saved 13-home-narrow-mobile-320.png`);
      await narrowCtx.close();
    } else if (targetFolder === 'be-data-p2') {
      // =========================================================================
      // BE/DATA-P2 Verified Real Movie Schema, Local Media & API Provenance Captures
      // =========================================================================
      console.log(`[Capture] Starting BE/DATA-P2 Verified Movie Content capture suite...`);
      runRunnerCommandOrThrow(['seed-verified-showcase'], state, {
        BGP_ALLOW_DEMO_SEED: 'true',
        BGP_DEMO_PASSWORD: state.demoPassword,
      });

      const desktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const desktopPage = await desktopCtx.newPage();

      // 1. /api/movies JSON response formatted cleanly in dark inspection panel
      const apiMoviesResp = await desktopPage.request.get('/api/movies');
      const apiMoviesJson = await apiMoviesResp.json();
      const charadeEntry = apiMoviesJson.find((m: any) => m.provenance?.wikidataQid === 'Q496266') ?? apiMoviesJson[0];
      const maiEntry = apiMoviesJson.find((m: any) => m.provenance?.wikidataQid === 'Q124516759');
      const summarizedPayload = {
        endpoint: 'GET /api/movies',
        totalPublishedVerifiedFilms: apiMoviesJson.length,
        fictionalEntriesCount: apiMoviesJson.filter((m: any) => String(m.title).includes('Phim mo phong')).length,
        heroEligibleSample: charadeEntry,
        pendingTmdbVnAgeRatedSample: maiEntry,
      };
      await desktopPage.setContent(`<!doctype html><html lang="vi"><head><meta charset="utf-8"><style>
        body { margin: 0; padding: 28px 36px; background: #0B0B0E; color: #F4F4F5; font-family: 'JetBrains Mono', Consolas, monospace; }
        h1 { font-family: 'Be Vietnam Pro', system-ui, sans-serif; font-size: 20px; margin: 0 0 8px; color: #FAFAFA; }
        p { font-family: 'Be Vietnam Pro', system-ui, sans-serif; font-size: 13px; margin: 0 0 18px; color: #A1A1AA; }
        pre { margin: 0; padding: 20px; background: #141418; border: 1px solid #27272A; border-radius: 10px; font-size: 12px; line-height: 1.5; white-space: pre-wrap; color: #E4E4E7; }
      </style></head><body>
        <h1>GET /api/movies — Dữ liệu Tác phẩm Thật &amp; Hồ sơ Nguồn Xác minh (BE/DATA-P2)</h1>
        <p>11/11 tác phẩm thật (Wikidata + IMDb Non-Commercial + Wikimedia Open Assets + Nhãn tuổi VN) • 0 phim hư cấu</p>
        <pre>${JSON.stringify(summarizedPayload, null, 2)}</pre>
      </body></html>`);
      await desktopPage.screenshot({
        path: path.join(outDir, '01-api-movies-verified-json.png'),
        fullPage: false,
      });
      console.log(`Saved 01-api-movies-verified-json.png`);

      // 2. /api/movies/{id}/quick-preview JSON response
      const previewResp = await desktopPage.request.get(`/api/movies/${charadeEntry.movieId}/quick-preview`);
      const previewJson = await previewResp.json();
      await desktopPage.setContent(`<!doctype html><html lang="vi"><head><meta charset="utf-8"><style>
        body { margin: 0; padding: 28px 36px; background: #0B0B0E; color: #F4F4F5; font-family: 'JetBrains Mono', Consolas, monospace; }
        h1 { font-family: 'Be Vietnam Pro', system-ui, sans-serif; font-size: 20px; margin: 0 0 8px; color: #FAFAFA; }
        p { font-family: 'Be Vietnam Pro', system-ui, sans-serif; font-size: 13px; margin: 0 0 18px; color: #A1A1AA; }
        pre { margin: 0; padding: 20px; background: #141418; border: 1px solid #27272A; border-radius: 10px; font-size: 12.5px; line-height: 1.52; white-space: pre-wrap; color: #E4E4E7; }
      </style></head><body>
        <h1>GET /api/movies/${charadeEntry.movieId}/quick-preview — Endpoint Đọc Nhẹ Cho Hover Preview</h1>
        <p>Trả về điểm IMDb kèm số lượt đánh giá, suất chiếu UTC+7 kế tiếp, mã trailer chính thức &amp; ảnh đa kích thước cục bộ</p>
        <pre>${JSON.stringify(previewJson, null, 2)}</pre>
      </body></html>`);
      await desktopPage.screenshot({
        path: path.join(outDir, '02-api-quick-preview-charade-json.png'),
        fullPage: false,
      });
      console.log(`Saved 02-api-quick-preview-charade-json.png`);

      // 3. Homepage Hero featuring real verified film (Mê trong mê / Charade 1963)
      await desktopPage.goto('/', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '03-home-hero-verified-charade-desktop.png'),
        fullPage: false,
      });
      console.log(`Saved 03-home-hero-verified-charade-desktop.png`);

      // 4. Full Movie Catalogue (/movies) with 11 real verified works + neutral placeholder for PENDING_TMDB_API_KEY
      await desktopPage.goto('/movies', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '04-catalog-verified-11-films-desktop.png'),
        fullPage: true,
      });
      console.log(`Saved 04-catalog-verified-11-films-desktop.png`);

      // 5. Filtered Catalogue (?filter=no-showtimes) highlighting Mai (T18) & Đào, phở và piano (T13) neutral "CHƯA CÓ HÌNH ẢNH" placeholder
      await desktopPage.goto('/movies?filter=no-showtimes', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '05-catalog-pending-tmdb-placeholder-desktop.png'),
        fullPage: false,
      });
      console.log(`Saved 05-catalog-pending-tmdb-placeholder-desktop.png`);

      await desktopCtx.close();
    } else if (targetFolder === 'ui-p1') {
      // =========================================================================
      // UI-P1 Netflix-Cinema Design System & Dual-Shell Captures
      // =========================================================================
      console.log(`[Capture] Starting UI-P1 Dual-Shell & Design System capture suite...`);
      runRunnerCommandOrThrow(['e2e-sql', 'seed-ui-fixtures'], state);

      // 1. Desktop Consumer Header — Top Translucent Overlay vs Scrolled Frosted Obsidian
      const desktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const desktopPage = await desktopCtx.newPage();
      await desktopPage.goto('/', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '01-cinema-header-top-overlay-desktop.png'),
        fullPage: false,
      });
      console.log(`Saved 01-cinema-header-top-overlay-desktop.png`);

      await desktopPage.evaluate(() => window.scrollTo({ top: 240, behavior: 'instant' as ScrollBehavior }));
      await desktopPage.waitForTimeout(260);
      await desktopPage.locator('[data-testid="header-search-input"]').focus();
      await desktopPage.locator('[data-testid="header-search-input"]').fill('Hành động');
      await desktopPage.waitForTimeout(200);
      await desktopPage.screenshot({
        path: path.join(outDir, '02-cinema-header-scrolled-search-desktop.png'),
        fullPage: false,
      });
      console.log(`Saved 02-cinema-header-scrolled-search-desktop.png`);

      // 2. Upgraded Zero-Bootstrap Static Shell Views (/privacy, /account/access-denied, /error)
      await desktopPage.goto('/privacy', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '03-privacy-provenance-desktop.png'),
        fullPage: true,
      });
      console.log(`Saved 03-privacy-provenance-desktop.png`);

      await desktopPage.goto('/account/access-denied', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '04-access-denied-desktop.png'),
        fullPage: false,
      });
      console.log(`Saved 04-access-denied-desktop.png`);

      await desktopPage.goto('/error?statusCode=404', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '05-error-404-desktop.png'),
        fullPage: false,
      });
      console.log(`Saved 05-error-404-desktop.png`);
      await desktopCtx.close();

      // 3. Mobile Consumer Drawer & 390x844 Privacy View
      const mobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
      });
      const mobilePage = await mobileCtx.newPage();
      await loginInBrowser(mobilePage, 'customer1@demo.invalid', state.demoPassword);
      await mobilePage.goto('/privacy', { waitUntil: 'networkidle' });
      await mobilePage.click('[data-testid="cinema-mobile-toggle"]');
      await mobilePage.waitForTimeout(280);
      await mobilePage.screenshot({
        path: path.join(outDir, '06-cinema-mobile-drawer-customer.png'),
        fullPage: false,
      });
      console.log(`Saved 06-cinema-mobile-drawer-customer.png`);
      await mobileCtx.close();

      // 4. Manager Context Switch on Consumer Shell & Operations Console (_OperationsLayout)
      const mgrCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const mgrPage = await mgrCtx.newPage();
      await loginInBrowser(mgrPage, 'manager@demo.invalid', state.demoPassword);
      await mgrPage.goto('/', { waitUntil: 'networkidle' });
      await mgrPage.screenshot({
        path: path.join(outDir, '07-cinema-header-manager-context-switch.png'),
        fullPage: false,
      });
      console.log(`Saved 07-cinema-header-manager-context-switch.png`);

      await mgrPage.goto('/management/movies', { waitUntil: 'networkidle' });
      await mgrPage.screenshot({
        path: path.join(outDir, '08-ops-shell-manager-desktop.png'),
        fullPage: false,
      });
      console.log(`Saved 08-ops-shell-manager-desktop.png`);
      await mgrCtx.close();

      // 5. Staff Operations Console Desktop & Mobile Drawer
      const staffDesktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const staffDesktopPage = await staffDesktopCtx.newPage();
      await loginInBrowser(staffDesktopPage, 'staff@demo.invalid', state.demoPassword);
      await staffDesktopPage.goto('/staff/check-in', { waitUntil: 'networkidle' });
      await staffDesktopPage.screenshot({
        path: path.join(outDir, '09-ops-shell-staff-desktop.png'),
        fullPage: false,
      });
      console.log(`Saved 09-ops-shell-staff-desktop.png`);
      await staffDesktopCtx.close();

      const staffMobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
      });
      const staffMobilePage = await staffMobileCtx.newPage();
      await loginInBrowser(staffMobilePage, 'staff@demo.invalid', state.demoPassword);
      await staffMobilePage.goto('/staff/check-in', { waitUntil: 'networkidle' });
      await staffMobilePage.click('[data-testid="ops-mobile-toggle"]');
      await staffMobilePage.waitForTimeout(280);
      await staffMobilePage.screenshot({
        path: path.join(outDir, '10-ops-shell-staff-mobile-drawer.png'),
        fullPage: false,
      });
      console.log(`Saved 10-ops-shell-staff-mobile-drawer.png`);
      await staffMobileCtx.close();

      runRunnerCommandOrThrow(['e2e-sql', 'reset-ui-fixtures'], state);
    } else if (targetFolder === 'ui-02b') {
      // =========================================================================
      // UI-02B Cinematic Movie Catalogue & Movie Details Captures
      // =========================================================================
      console.log(`[Capture] Starting UI-02B Movie Discovery capture suite...`);

      runRunnerCommandOrThrow(['e2e-sql', 'seed-ui-fixtures'], state);

      // 1. Desktop Catalogue (1440x900)
      const desktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const desktopPage = await desktopCtx.newPage();
      await desktopPage.goto('/movies', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '01-catalog-desktop.png'),
        fullPage: true,
      });
      console.log(`Saved 01-catalog-desktop.png`);

      // 2. Mobile Catalogue (390x844)
      const mobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
      });
      const mobilePage = await mobileCtx.newPage();
      await mobilePage.goto('/movies', { waitUntil: 'networkidle' });
      await mobilePage.screenshot({
        path: path.join(outDir, '02-catalog-mobile.png'),
        fullPage: true,
      });
      console.log(`Saved 02-catalog-mobile.png`);

      // 3. Catalogue with Search / Filter active (1440x900)
      await desktopPage.goto('/movies?filter=has-showtimes&q=Chien+Binh', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '03-catalog-search-filter.png'),
        fullPage: false,
      });
      console.log(`Saved 03-catalog-search-filter.png`);

      // 4. Catalogue with No Matching Results (1440x900)
      await desktopPage.goto('/movies?q=KhongCoPhimNay12345', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '04-catalog-no-results.png'),
        fullPage: false,
      });
      console.log(`Saved 04-catalog-no-results.png`);

      // Find movie ID with multi-date showtimes ("Chien Binh Khong Gian") and zero showtimes ("Phim Khong Co Suat")
      await desktopPage.goto('/movies', { waitUntil: 'networkidle' });
      const multiShowtimeHref = await desktopPage
        .locator('[data-testid="catalog-movie-card"]', { hasText: 'Chien Binh Khong Gian' })
        .first()
        .getAttribute('href');
      const zeroShowtimeHref = await desktopPage
        .locator('[data-testid="catalog-movie-card"]', { hasText: 'Phim Khong Co Suat' })
        .first()
        .getAttribute('href');

      if (!multiShowtimeHref || !zeroShowtimeHref) {
        throw new Error(`[Capture] Expected fixture movie cards not found on /movies.`);
      }

      // 5. Movie Details Desktop with Showtimes (1440x900)
      await desktopPage.goto(multiShowtimeHref, { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '05-detail-desktop.png'),
        fullPage: true,
      });
      console.log(`Saved 05-detail-desktop.png`);

      // 6. Movie Details Mobile (390x844)
      await mobilePage.goto(multiShowtimeHref, { waitUntil: 'networkidle' });
      await mobilePage.screenshot({
        path: path.join(outDir, '06-detail-mobile.png'),
        fullPage: true,
      });
      console.log(`Saved 06-detail-mobile.png`);
      await mobileCtx.close();

      // 7. Movie Details with Zero Showtimes (1440x900)
      await desktopPage.goto(zeroShowtimeHref, { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '07-detail-no-showtimes.png'),
        fullPage: true,
      });
      console.log(`Saved 07-detail-no-showtimes.png`);

      // 8. Poster Fallback & Long Title State on Catalogue / Details (1440x900)
      runRunnerCommandOrThrow(['e2e-sql', 'set-long-title'], state);
      await desktopPage.goto('/movies', { waitUntil: 'networkidle' });
      await desktopPage.screenshot({
        path: path.join(outDir, '08-poster-fallback-long-title.png'),
        fullPage: false,
      });
      console.log(`Saved 08-poster-fallback-long-title.png`);
      runRunnerCommandOrThrow(['e2e-sql', 'restore-title'], state);

      // 9. Showtime Date Selector & Grouped Showtime Cards (select 2nd date tab & focus)
      await desktopPage.goto(multiShowtimeHref, { waitUntil: 'networkidle' });
      const dateTabs = desktopPage.locator('[data-testid="showtime-date-tab"]');
      if ((await dateTabs.count()) > 1) {
        await dateTabs.nth(1).click();
        await dateTabs.nth(1).focus();
        await desktopPage.waitForTimeout(250);
      }
      const showtimesSection = desktopPage.locator('#showtimes-section');
      await showtimesSection.screenshot({
        path: path.join(outDir, '09-showtime-date-selector.png'),
      });
      console.log(`Saved 09-showtime-date-selector.png`);

      runRunnerCommandOrThrow(['e2e-sql', 'reset-ui-fixtures'], state);
      await desktopCtx.close();
    } else if (targetFolder === 'ui-02a') {
      // =========================================================================
      // UI-02A Cinematic Homepage & Movie Artwork Discovery Captures
      // =========================================================================
      console.log(`[Capture] Starting UI-02A Cinematic Homepage capture suite...`);

      // Seed extra movies into ephemeral DB so the rail has multiple rich items
      runRunnerCommandOrThrow(['e2e-sql', 'seed-ui-fixtures'], state);

      // 1. Desktop full homepage (1440x900)
      const desktopCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const desktopPage = await desktopCtx.newPage();
      await desktopPage.goto('/');
      await desktopPage.waitForLoadState('networkidle');
      await desktopPage.screenshot({
        path: path.join(outDir, '01-desktop-home-full.png'),
        fullPage: true,
      });
      console.log(`Saved 01-desktop-home-full.png`);

      // 2. Mobile full homepage (390x844)
      const mobileCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
      });
      const mobilePage = await mobileCtx.newPage();
      await mobilePage.goto('/');
      await mobilePage.waitForLoadState('networkidle');
      await mobilePage.screenshot({
        path: path.join(outDir, '02-mobile-home-full.png'),
        fullPage: true,
      });
      console.log(`Saved 02-mobile-home-full.png`);
      await mobileCtx.close();

      // 3. Tablet homepage (768x1024)
      const tabletCtx = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 768, height: 1024 },
      });
      const tabletPage = await tabletCtx.newPage();
      await tabletPage.goto('/');
      await tabletPage.waitForLoadState('networkidle');
      await tabletPage.screenshot({
        path: path.join(outDir, '03-tablet-home.png'),
        fullPage: true,
      });
      console.log(`Saved 03-tablet-home.png`);
      await tabletCtx.close();

      // 4. Hero section close-up (1440x900)
      const heroEl = desktopPage.locator('.bgp-home-hero');
      if (await heroEl.isVisible()) {
        await heroEl.screenshot({
          path: path.join(outDir, '04-hero-cinematic.png'),
        });
        console.log(`Saved 04-hero-cinematic.png`);
      }

      // 4b. Hero with long title: update title in DB temporarily
      runRunnerCommandOrThrow(['e2e-sql', 'set-long-title'], state);
      await desktopPage.goto(state.baseUrl + '/?v=' + Date.now(), { waitUntil: 'networkidle' });
      if (await heroEl.isVisible()) {
        await heroEl.screenshot({
          path: path.join(outDir, '04b-hero-long-title.png'),
        });
        console.log(`Saved 04b-hero-long-title.png`);
      }
      // Restore original title
      runRunnerCommandOrThrow(['e2e-sql', 'restore-title'], state);
      await desktopPage.goto(state.baseUrl + '/?v=' + Date.now());
      await desktopPage.waitForLoadState('networkidle');

      // 5. Movie rail keyboard focus state
      const railSection = desktopPage.locator('.bgp-movie-rail-section');
      const cards = desktopPage.locator('.bgp-movie-card');
      if ((await cards.count()) > 1) {
        await cards.nth(1).focus();
        await desktopPage.waitForTimeout(300);
        await railSection.screenshot({
          path: path.join(outDir, '05-movie-rail-keyboard-focus.png'),
        });
        console.log(`Saved 05-movie-rail-keyboard-focus.png`);
      }

      // 6. Empty state: set movies to DRAFT
      runRunnerCommandOrThrow(['e2e-sql', 'hide-all-movies'], state);
      await desktopPage.reload();
      await desktopPage.waitForLoadState('networkidle');
      await desktopPage.screenshot({
        path: path.join(outDir, '06-empty-state.png'),
        fullPage: false,
      });
      console.log(`Saved 06-empty-state.png`);
      // Restore movies to previous statuses
      runRunnerCommandOrThrow(['e2e-sql', 'restore-all-movies'], state);
      await desktopPage.reload();
      await desktopPage.waitForLoadState('networkidle');

      // 7. Artwork fallback: simulate broken image URL on first card poster
      await desktopPage.evaluate(() => {
        const img = document.querySelector('.bgp-movie-card img') as HTMLImageElement;
        if (img) {
          img.src = '/images/non-existent-broken-poster-404.jpg';
        }
      });
      await desktopPage.waitForTimeout(500);
      const firstCard = desktopPage.locator('.bgp-movie-card').first();
      if (await firstCard.isVisible()) {
        await firstCard.screenshot({
          path: path.join(outDir, '07-artwork-fallback.png'),
        });
        console.log(`Saved 07-artwork-fallback.png`);
      }

      // 8. Upcoming showtimes card
      const upcomingCard = desktopPage.locator('.bgp-upcoming-card');
      if (await upcomingCard.isVisible()) {
        await upcomingCard.screenshot({
          path: path.join(outDir, '08-upcoming-showtimes.png'),
        });
        console.log(`Saved 08-upcoming-showtimes.png`);
      }

      runRunnerCommandOrThrow(['e2e-sql', 'reset-ui-fixtures'], state);
      await desktopCtx.close();
    } else {
      // =========================================================================
      // UI-01A Specific Targeted Verification & Evidence Captures
      // =========================================================================

      // 1. Evidence Fix A: Bright badge contrast on Movies page (1440x900)
      const moviesContext = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const moviesPage = await moviesContext.newPage();
      await moviesPage.goto('/movies');
      await moviesPage.waitForLoadState('networkidle');

      const badgeEvidence = await moviesPage.evaluate(() => {
        const warningBadge = document.querySelector('.badge.bg-warning');
        const infoBadge = document.querySelector('.badge.bg-info');
        const lightBadge = document.querySelector('.badge.bg-light');
        return {
          warningColor: warningBadge ? window.getComputedStyle(warningBadge).color : null,
          infoColor: infoBadge ? window.getComputedStyle(infoBadge).color : null,
          lightColor: lightBadge ? window.getComputedStyle(lightBadge).color : null,
        };
      });
      console.log(`[Evidence Fix A] Computed badge colors:`, badgeEvidence);

      await moviesPage.screenshot({
        path: path.join(outDir, '01-movies-badges.png'),
        fullPage: false,
      });
      console.log(`Saved 01-movies-badges.png`);
      await moviesContext.close();

      // 2. Evidence Fix B: Outline buttons & focus visibility
      const buttonsContext = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const buttonsPage = await buttonsContext.newPage();
      await buttonsPage.goto('/account/login');
      await buttonsPage.waitForLoadState('networkidle');

      const submitBtn = buttonsPage.locator('button[type="submit"]');
      if (await submitBtn.isVisible()) {
        await submitBtn.focus();
      }
      await buttonsPage.screenshot({
        path: path.join(outDir, '02-outline-buttons.png'),
        fullPage: false,
      });
      console.log(`Saved 02-outline-buttons.png`);
      await buttonsContext.close();

      // 3. Evidence Fix C: Form controls (>= 3:1 border) and validation typography
      const validationContext = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const validationPage = await validationContext.newPage();
      await validationPage.goto('/account/login');
      await validationPage.waitForLoadState('networkidle');

      await validationPage.click('button[type="submit"]');
      await validationPage.waitForTimeout(400);

      await validationPage.locator('input[name="Request.Email"]').focus();

      const inputEvidence = await validationPage.evaluate(() => {
        const emailInput = document.querySelector('input[name="Request.Email"]');
        const valError = document.querySelector('.field-validation-error, .invalid-feedback');
        return {
          inputBorderColor: emailInput ? window.getComputedStyle(emailInput).borderColor : null,
          validationFontSize: valError ? window.getComputedStyle(valError).fontSize : null,
        };
      });
      console.log(`[Evidence Fix B & C] Computed input & validation styles:`, inputEvidence);

      await validationPage.screenshot({
        path: path.join(outDir, '03-form-inputs-validation.png'),
        fullPage: false,
      });
      console.log(`Saved 03-form-inputs-validation.png`);
      await validationContext.close();

      // 4. Tablet Viewport (768x1024) - Guest Home
      const tabletContext = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 768, height: 1024 },
      });
      const tabletPage = await tabletContext.newPage();
      await tabletPage.goto('/');
      await tabletPage.waitForLoadState('networkidle');
      await tabletPage.screenshot({
        path: path.join(outDir, '04-tablet-768x1024.png'),
        fullPage: false,
      });
      console.log(`Saved 04-tablet-768x1024.png`);
      await tabletContext.close();

      // 5. Desktop Near Breakpoint (992x768)
      const breakpointContext = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 992, height: 768 },
      });
      const breakpointPage = await breakpointContext.newPage();
      await breakpointPage.goto('/');
      await breakpointPage.waitForLoadState('networkidle');
      await breakpointPage.screenshot({
        path: path.join(outDir, '05-desktop-near-992.png'),
        fullPage: false,
      });
      console.log(`Saved 05-desktop-near-992.png`);
      await breakpointContext.close();

      // 6. Admin Role: Management Reports (1440x900)
      const adminContext = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1440, height: 900 },
      });
      const adminPage = await adminContext.newPage();
      await loginInBrowser(adminPage, 'admin@demo.invalid', state.demoPassword);
      await adminPage.goto('/management/reports');
      await adminPage.waitForLoadState('networkidle');
      await adminPage.screenshot({
        path: path.join(outDir, '06-admin-reports.png'),
        fullPage: false,
      });
      console.log(`Saved 06-admin-reports.png`);
      await adminContext.close();

      // 7. Admin Mobile Menu (390x844)
      const adminMobileContext = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
      });
      const adminMobilePage = await adminMobileContext.newPage();
      await loginInBrowser(adminMobilePage, 'admin@demo.invalid', state.demoPassword);
      await adminMobilePage.goto('/');
      await adminMobilePage.waitForLoadState('networkidle');
      const adminToggler = adminMobilePage.locator('.navbar-toggler');
      if (await adminToggler.isVisible()) {
        await adminToggler.click();
        await adminMobilePage.waitForTimeout(500);
        await adminMobilePage.screenshot({
          path: path.join(outDir, '07-admin-mobile-menu.png'),
          fullPage: false,
        });
        console.log(`Saved 07-admin-mobile-menu.png`);
      }
      await adminMobileContext.close();

      // 8. Staff Mobile Menu (390x844)
      const staffMobileContext = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 390, height: 844 },
      });
      const staffMobilePage = await staffMobileContext.newPage();
      await loginInBrowser(staffMobilePage, 'staff@demo.invalid', state.demoPassword);
      await staffMobilePage.goto('/');
      await staffMobilePage.waitForLoadState('networkidle');
      const staffToggler = staffMobilePage.locator('.navbar-toggler');
      if (await staffToggler.isVisible()) {
        await staffToggler.click();
        await staffMobilePage.waitForTimeout(500);
        await staffMobilePage.screenshot({
          path: path.join(outDir, '08-staff-mobile-menu.png'),
          fullPage: false,
        });
        console.log(`Saved 08-staff-mobile-menu.png`);
      }
      await staffMobileContext.close();

      // 9. Zoom 200% on Representative Page (Movies, 1280x800 base)
      const zoomContext = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 1280, height: 800 },
      });
      const zoomPage = await zoomContext.newPage();
      await zoomPage.goto('/movies');
      await zoomPage.waitForLoadState('networkidle');
      await zoomPage.evaluate(() => {
        document.body.style.zoom = '200%';
      });
      await zoomPage.waitForTimeout(400);
      await zoomPage.screenshot({
        path: path.join(outDir, '09-zoom-200.png'),
        fullPage: false,
      });
      console.log(`Saved 09-zoom-200.png`);
      await zoomContext.close();

      // 10. Narrow Mobile (320px width strict assertion)
      const narrowContext = await browser.newContext({
        baseURL: state.baseUrl,
        viewport: { width: 320, height: 600 },
      });
      const narrowPage = await narrowContext.newPage();
      await narrowPage.goto('/');
      await narrowPage.waitForLoadState('networkidle');

      const overflowResult = await narrowPage.evaluate(() => {
        const docScrollWidth = document.documentElement.scrollWidth;
        const docClientWidth = document.documentElement.clientWidth;
        const bodyScrollWidth = document.body.scrollWidth;
        const bodyClientWidth = document.body.clientWidth;
        return {
          docScrollWidth,
          docClientWidth,
          bodyScrollWidth,
          bodyClientWidth,
          noDocOverflow: docScrollWidth <= docClientWidth,
          noBodyOverflow: bodyScrollWidth <= bodyClientWidth,
        };
      });
      console.log(`[Evidence 320px] Shell overflow measurement:`, overflowResult);
      if (!overflowResult.noDocOverflow || !overflowResult.noBodyOverflow) {
        console.warn(`[Capture] WARNING: 320px horizontal overflow detected!`, overflowResult);
      }

      await narrowPage.screenshot({
        path: path.join(outDir, '10-narrow-320px.png'),
        fullPage: false,
      });
      console.log(`Saved 10-narrow-320px.png`);
      await narrowContext.close();
    }
  } catch (err) {
    originalError = err;
    console.error('[Capture] Execution error:', err);
  } finally {
    // Phase A: Close browser safely; failure here MUST NOT prevent Kestrel/DB teardown
    if (browser) {
      try {
        await browser.close();
      } catch (browserCloseErr) {
        console.error('[Capture] Warning: browser.close() threw error:', browserCloseErr);
      }
    }

    // Phase B: Always teardown web server and drop ephemeral DB if setup completed
    if (setupCompleted) {
      try {
        console.log(`[Capture] Tearing down test web server and database (${state.dbName})...`);
        await runGlobalTeardownLifecycle(stateFilePath, { forceAutoCleanup: true });
        console.log(`[Capture] Teardown lifecycle completed cleanly.`);
      } catch (teardownErr) {
        console.error('[Capture] Error during teardown lifecycle:', teardownErr);
        if (!originalError) {
          originalError = teardownErr;
        }
      }
    }

    // Phase C: Propagate original capture error if one occurred
    if (originalError) {
      throw originalError;
    }

    return { state };
  }
}

if (require.main === module) {
  runUiCapture().catch((err) => {
    console.error(err);
    process.exit(1);
  });
}
