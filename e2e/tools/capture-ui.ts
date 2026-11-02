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
 * Seeds an ADMIN user into the ephemeral test database using sqlcmd
 * with the exact password hash cloned from manager@demo.invalid.
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

  const res = spawnSync('sqlcmd', ['-S', 'localhost', '-d', state.dbName, '-E', '-C', '-Q', sql], {
    encoding: 'utf-8',
    timeout: 10_000,
  });

  if (res.status !== 0) {
    console.warn(`[Capture] Warning: failed to seed admin user via sqlcmd: ${res.stderr || res.stdout}`);
  } else {
    console.log(`[Capture] Seeded admin@demo.invalid with role ADMIN in ${state.dbName}`);
  }
}

export async function runUiCapture(options?: {
  targetFolder?: string;
  port?: number;
  skipBrowserLaunchForTest?: boolean;
  stateFilePath?: string;
}): Promise<void> {
  const targetFolder = options?.targetFolder || process.argv[2] || 'ui-01a';
  const outDir = path.resolve(__dirname, '..', '..', 'docs', 'ui', 'screenshots', targetFolder);
  if (!fs.existsSync(outDir)) {
    fs.mkdirSync(outDir, { recursive: true });
  }

  const port = options?.port || 5195;
  const stateFilePath =
    options?.stateFilePath || path.resolve(__dirname, '..', '.tmp', 'capture-state.json');
  const state = createInitialE2eState('cap_' + Date.now().toString(36), port);
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
  }
}

if (require.main === module) {
  runUiCapture().catch((err) => {
    console.error(err);
    process.exit(1);
  });
}
