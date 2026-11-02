import { chromium } from '@playwright/test';
import * as path from 'path';
import * as fs from 'fs';
import {
  createInitialE2eState,
  runGlobalSetupLifecycle,
  runGlobalTeardownLifecycle,
  loadE2eState,
  getStateFilePath,
} from '../e2e-env';
import { loginInBrowser } from '../tests/helpers';

async function main() {
  const targetFolder = process.argv[2] || 'baseline';
  const outDir = path.resolve(__dirname, '..', '..', 'docs', 'ui', 'screenshots', targetFolder);
  if (!fs.existsSync(outDir)) {
    fs.mkdirSync(outDir, { recursive: true });
  }

  const state = createInitialE2eState('cap_' + Date.now().toString(36), 5195);
  console.log(`[Capture] Launching test web server on port 5195 with ephemeral DB: ${state.dbName}`);
  await runGlobalSetupLifecycle(state);

  const browser = await chromium.launch();
  try {
    // 1. Desktop Guest Home (1440x900)
    const guestDesktopContext = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const guestDesktopPage = await guestDesktopContext.newPage();
    await guestDesktopPage.goto('/');
    await guestDesktopPage.waitForLoadState('networkidle');
    await guestDesktopPage.screenshot({ path: path.join(outDir, '01-guest-home-desktop.png'), fullPage: false });
    console.log(`Saved 01-guest-home-desktop.png`);

    // 2. Mobile Guest Home (390x844)
    const guestMobileContext = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 390, height: 844 },
    });
    const guestMobilePage = await guestMobileContext.newPage();
    await guestMobilePage.goto('/');
    await guestMobilePage.waitForLoadState('networkidle');
    await guestMobilePage.screenshot({ path: path.join(outDir, '02-guest-home-mobile.png'), fullPage: false });
    console.log(`Saved 02-guest-home-mobile.png`);

    // 2b. Narrow Mobile (320px width check)
    const narrowMobileContext = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 320, height: 600 },
    });
    const narrowMobilePage = await narrowMobileContext.newPage();
    await narrowMobilePage.goto('/');
    await narrowMobilePage.waitForLoadState('networkidle');
    const scrollWidth = await narrowMobilePage.evaluate(() => document.documentElement.scrollWidth);
    console.log(`Narrow 320px viewport scrollWidth: ${scrollWidth}`);
    await narrowMobilePage.screenshot({ path: path.join(outDir, '08-narrow-320px.png'), fullPage: false });
    console.log(`Saved 08-narrow-320px.png`);
    await narrowMobileContext.close();

    // 3. Mobile Menu Open (390x844)
    const toggler = guestMobilePage.locator('.navbar-toggler');
    if (await toggler.isVisible()) {
      await toggler.click();
      await guestMobilePage.waitForTimeout(500);
      await guestMobilePage.screenshot({ path: path.join(outDir, '03-mobile-menu-open.png'), fullPage: false });
      console.log(`Saved 03-mobile-menu-open.png`);
    }

    // 4. Customer Orders Page (1440x900)
    const customerContext = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const customerPage = await customerContext.newPage();
    await loginInBrowser(customerPage, 'customer1@demo.invalid', state.demoPassword);
    await customerPage.goto('/orders');
    await customerPage.waitForLoadState('networkidle');
    await customerPage.screenshot({ path: path.join(outDir, '04-customer-orders.png'), fullPage: false });
    console.log(`Saved 04-customer-orders.png`);

    // 5. Staff Check-In Page (1440x900)
    const staffContext = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const staffPage = await staffContext.newPage();
    await loginInBrowser(staffPage, 'staff@demo.invalid', state.demoPassword);
    await staffPage.goto('/staff/check-in');
    await staffPage.waitForLoadState('networkidle');
    await staffPage.screenshot({ path: path.join(outDir, '05-staff-checkin.png'), fullPage: false });
    console.log(`Saved 05-staff-checkin.png`);

    // 6. Manager Movies Page (1440x900)
    const managerContext = await browser.newContext({
      baseURL: state.baseUrl,
      viewport: { width: 1440, height: 900 },
    });
    const managerPage = await managerContext.newPage();
    await loginInBrowser(managerPage, 'manager@demo.invalid', state.demoPassword);
    await managerPage.goto('/management/movies');
    await managerPage.waitForLoadState('networkidle');
    await managerPage.screenshot({ path: path.join(outDir, '06-manager-movies.png'), fullPage: false });
    console.log(`Saved 06-manager-movies.png`);

    // 7. Manager Reports Page (1440x900)
    await managerPage.goto('/management/reports');
    await managerPage.waitForLoadState('networkidle');
    await managerPage.screenshot({ path: path.join(outDir, '07-manager-reports.png'), fullPage: false });
    console.log(`Saved 07-manager-reports.png`);

    await guestDesktopContext.close();
    await guestMobileContext.close();
    await customerContext.close();
    await staffContext.close();
    await managerContext.close();
  } finally {
    await browser.close();
    console.log(`[Capture] Tearing down test web server and database...`);
    await runGlobalTeardownLifecycle(getStateFilePath(), { forceAutoCleanup: true });
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
