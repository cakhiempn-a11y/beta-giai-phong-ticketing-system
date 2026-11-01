import { FullConfig } from '@playwright/test';
import * as fs from 'fs';
import { getStateFilePath, loadE2eState, runRunnerCommand } from './e2e-env';

async function globalTeardown(_config: FullConfig): Promise<void> {
  const stateFile = getStateFilePath();
  if (!fs.existsSync(stateFile)) {
    return;
  }

  const state = loadE2eState();

  if (state.webPid) {
    console.log(`[E2E Teardown] Stopping BetaGiaiPhong.Web (PID: ${state.webPid})...`);
    try {
      process.kill(state.webPid);
    } catch {
      // Process may have already exited
    }
    await new Promise((r) => setTimeout(r, 1000));
  }

  console.log(`[E2E Teardown] Running Runner verify against ${state.dbName}...`);
  const verifyRes = runRunnerCommand(['verify'], state);
  console.log(verifyRes.stdout.trim());
  if (verifyRes.status !== 0) {
    console.error(`[E2E Teardown] Integrity verification failed:\n${verifyRes.stderr}`);
  }

  const autoCleanup = process.env.BGP_TEST_AUTO_CLEANUP_DB;
  if (autoCleanup && autoCleanup.toLowerCase() === 'false') {
    console.log(`[E2E Teardown] BGP_TEST_AUTO_CLEANUP_DB=false — preserving database ${state.dbName}`);
    return;
  }

  console.log(`[E2E Teardown] Dropping ephemeral E2E database ${state.dbName}...`);
  const dropRes = runRunnerCommand(
    ['drop-e2e-db', state.dbName, state.ownershipToken],
    state
  );
  if (dropRes.status !== 0) {
    console.error(`[E2E Teardown] Failed to drop E2E database ${state.dbName}:\n${dropRes.stderr}`);
  } else {
    console.log(`[E2E Teardown] ${dropRes.stdout.trim()}`);
  }
}

export default globalTeardown;
