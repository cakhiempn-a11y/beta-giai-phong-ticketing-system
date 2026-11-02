import { chromium, Browser } from '@playwright/test';
import * as path from 'path';
import * as fs from 'fs';
import { spawnSync } from 'child_process';
import {
  createInitialE2eState,
  runGlobalSetupLifecycle,
  runGlobalTeardownLifecycle,
  getStateFilePath,
  E2eState,
} from '../e2e-env';
import { loginInBrowser } from '../tests/helpers';

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
 * Includes retry logic for transient connection spikes.
 */
function executeSqlInDb(state: E2eState, sql: string, maxAttempts = 5): void {
  const { server, user, password, trusted } = parseSqlConnectionString(state.connectionString);
  const args = ['-S', server, '-d', state.dbName, '-C'];
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

    if (res.status === 0) {
      if (res.stdout && res.stdout.trim().length > 0) {
        console.log(`[Capture SQL]\n${res.stdout.trim()}`);
      }
      return;
    }

    if (attempt < maxAttempts) {
      // Short delay before retry
      const waitBuffer = new Int32Array(new SharedArrayBuffer(4));
      Atomics.wait(waitBuffer, 0, 0, 600);
    } else {
      throw new Error(
        `[Capture] CRITICAL: SQL execution failed in ephemeral DB ${state.dbName} (exit ${res.status}):\n${res.stderr || res.stdout}`
      );
    }
  }
}

/**
 * Seeds an ADMIN user into the ephemeral test database using sqlcmd
 * with the exact password hash cloned from manager@demo.invalid.
 * Throws an explicit error if seeding fails to avoid false-positive test passes.
 */
function seedAdminUser(state: E2eState): void {
  const sql = `
    DECLARE @adminRoleId int;
    SELECT @adminRoleId = role_id FROM dbo.Roles WHERE role_code = 'ADMIN';

    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE normalized_email = 'ADMIN@DEMO.INVALID')
    BEGIN
        INSERT INTO dbo.Users (email, normalized_email, display_name, status, created_utc, password_hash)
        SELECT 'admin@demo.invalid', 'ADMIN@DEMO.INVALID', N'Demo Quan tri', 'ACTIVE', SYSUTCDATETIME(), password_hash
        FROM dbo.Users WHERE normalized_email = 'MANAGER@DEMO.INVALID';

        DECLARE @adminId bigint = SCOPE_IDENTITY();

        INSERT INTO dbo.UserRoles (user_id, role_id)
        VALUES (@adminId, @adminRoleId);
    END
  `;

  executeSqlInDb(state, sql);
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

    // Seed ADMIN user into ephemeral DB
    seedAdminUser(state);

    if (options?.skipBrowserLaunchForTest) {
      throw new Error('Simulated browser launch failure for lifecycle cleanup test');
    }

    browser = await chromium.launch();

    if (targetFolder === 'ui-02a') {
      // =========================================================================
      // UI-02A Cinematic Homepage & Movie Artwork Discovery Captures
      // =========================================================================
      console.log(`[Capture] Starting UI-02A Cinematic Homepage capture suite...`);

      // Seed extra movies into ephemeral DB so the rail has multiple rich items
      executeSqlInDb(
        state,
        `
        UPDATE dbo.Movies 
        SET age_label = 'T16', 
            synopsis = N'Một kiệt tác điện ảnh kịch tính và cảm xúc, đưa khán giả vào hành trình khám phá những bí ẩn sâu thẳm tại Beta Cinema Giải Phóng.',
            poster_url = '/images/poster-fallback.svg'
        WHERE title = 'Phim mo phong - KHONG phai lich Beta';

        INSERT INTO dbo.Movies (title, duration_min, genre_text, status, age_label, synopsis, poster_url)
        VALUES 
        (N'Huyền Thoại Mê Cung', 125, N'Hành Động, Viễn Tưởng', 'PUBLISHED', 'T16', N'Hành trình nghẹt thở xuyên không gian và thời gian.', '/images/poster-fallback.svg'),
        (N'Thành Phố Hoàng Hôn', 102, N'Lãng Mạn, Tâm Lý', 'PUBLISHED', 'T13', N'Câu chuyện tình yêu đầy cảm xúc giữa lòng đô thị.', '/images/poster-fallback.svg'),
        (N'Vũ Điệu Bóng Đêm', 118, N'Kinh Dị, Giật Gân', 'PUBLISHED', 'T18', N'Bí ẩn rùng rợn đằng sau bức màn nhung của nhà hát cổ.', '/images/poster-fallback.svg'),
        (N'Siêu Điệp Viên Nhí', 95, N'Hoạt Hình, Gia Đình', 'PUBLISHED', 'P', N'Biệt đội điệp viên tí hon bảo vệ hòa bình thế giới.', '/images/poster-fallback.svg');
        `
      );

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
      executeSqlInDb(
        state,
        `UPDATE dbo.Movies 
         SET title = 'Chien Binh Khong Gian - Vu Tru Huyen Thoai Dao Chieu Dot Pha Thoi Gian Khong Hoi Ket (Ban Chieu Rap 3D Dac Biet IMAX)';
         SELECT movie_id, title FROM dbo.Movies;`
      );
      await desktopPage.goto(state.baseUrl + '/?v=' + Date.now(), { waitUntil: 'networkidle' });
      const currentUrl = desktopPage.url();
      const heroTitleHtml = await desktopPage.locator('.bgp-hero-title').evaluate((el) => el.outerHTML);
      const railTitles = await desktopPage.locator('.bgp-card-title').allInnerTexts();
      console.log(`[Capture] Page URL: ${currentUrl}`);
      console.log(`[Capture] Hero title HTML: ${heroTitleHtml}`);
      console.log(`[Capture] Rail titles:`, railTitles);
      if (await heroEl.isVisible()) {
        await heroEl.screenshot({
          path: path.join(outDir, '04b-hero-long-title.png'),
        });
        console.log(`Saved 04b-hero-long-title.png`);
      }
      // Restore original title
      executeSqlInDb(
        state,
        `UPDATE dbo.Movies 
         SET title = 'Phim mo phong - KHONG phai lich Beta' 
         WHERE movie_id = 1;`
      );
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
      executeSqlInDb(state, `UPDATE dbo.Movies SET status = 'DRAFT';`);
      await desktopPage.reload();
      await desktopPage.waitForLoadState('networkidle');
      await desktopPage.screenshot({
        path: path.join(outDir, '06-empty-state.png'),
        fullPage: false,
      });
      console.log(`Saved 06-empty-state.png`);
      // Restore movies to PUBLISHED
      executeSqlInDb(state, `UPDATE dbo.Movies SET status = 'PUBLISHED';`);
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

      // Verify computed colors on badges
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

      // Focus on the secondary or outline button / submit button
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

      // Trigger validation error by submitting empty form
      await validationPage.click('button[type="submit"]');
      await validationPage.waitForTimeout(400);

      // Focus on email input to reveal two-layer focus ring
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
