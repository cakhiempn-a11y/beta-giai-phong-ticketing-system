import { test, expect } from '@playwright/test';
import * as path from 'path';
import * as fs from 'fs';
import { runUiCapture } from '../tools/capture-ui';
import { createInitialE2eState, runRunnerCommand } from '../e2e-env';

test.describe('UI-01A Fix D / UI-02A: Capture Tool Lifecycle and Hardened Cleanup', () => {
  test('Capture tool teardown drops ephemeral database and terminates Kestrel on simulated failure', async () => {
    const testPort = 5199;
    const customStateFile = path.resolve(__dirname, '..', '.tmp', 'test-capture-cleanup-state.json');

    // Ensure state file from any previous failed run is cleaned
    if (fs.existsSync(customStateFile)) {
      fs.unlinkSync(customStateFile);
    }

    const testState = createInitialE2eState('cap_fail_' + Date.now().toString(36), testPort);
    let captureError: Error | null = null;
    try {
      await runUiCapture({
        targetFolder: 'test-tmp',
        port: testPort,
        skipBrowserLaunchForTest: true,
        stateFilePath: customStateFile,
        existingState: testState,
      });
    } catch (err: any) {
      captureError = err;
    }

    // 1. Verify that the original error was preserved and thrown
    expect(captureError).not.toBeNull();
    expect(captureError?.message).toContain('Simulated browser launch failure');

    // 2. Verify that teardown cleaned up the state file
    expect(fs.existsSync(customStateFile)).toBe(false);

    // 3. Directly verify against SQL Server using runner that the database was dropped!
    const dbCheck = runRunnerCommand(['e2e-sql', 'check-db-exists'], testState);
    expect(dbCheck.status).toBe(0);
    expect(dbCheck.stdout).toContain('DB_EXISTS:0');

    // 4. Verify that the Kestrel web server on testPort was terminated (connection actively refused)
    let isServerDown = false;
    let transportError: any = null;
    try {
      await fetch(`http://127.0.0.1:${testPort}/health`);
    } catch (err: any) {
      transportError = err;
      isServerDown = true;
    }
    expect(isServerDown).toBe(true);
    expect(transportError).not.toBeNull();

    // 5. Verify process death if PID was captured
    if (testState.webPid) {
      let isProcessDead = false;
      try {
        process.kill(testState.webPid, 0);
      } catch {
        isProcessDead = true;
      }
      expect(isProcessDead).toBe(true);
    }

    // Clean up temporary screenshots test folder if created
    const tmpDir = path.resolve(__dirname, '..', '..', 'docs', 'ui', 'screenshots', 'test-tmp');
    if (fs.existsSync(tmpDir)) {
      try {
        fs.rmSync(tmpDir, { recursive: true, force: true });
      } catch {}
    }
  });
});
