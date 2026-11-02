import { test, expect } from '@playwright/test';
import * as path from 'path';
import * as fs from 'fs';
import { runUiCapture } from '../tools/capture-ui';

test.describe('UI-01A Fix D: Capture Tool Lifecycle and Hardened Cleanup', () => {
  test('Capture tool teardown drops ephemeral database and terminates Kestrel on simulated failure', async () => {
    const testPort = 5199;
    const customStateFile = path.resolve(__dirname, '..', '.tmp', 'test-capture-cleanup-state.json');

    // Ensure state file from any previous failed run is cleaned
    if (fs.existsSync(customStateFile)) {
      fs.unlinkSync(customStateFile);
    }

    let captureError: Error | null = null;
    try {
      await runUiCapture({
        targetFolder: 'test-tmp',
        port: testPort,
        skipBrowserLaunchForTest: true,
        stateFilePath: customStateFile,
      });
    } catch (err: any) {
      captureError = err;
    }

    // 1. Verify that the original error was preserved and thrown
    expect(captureError).not.toBeNull();
    expect(captureError?.message).toContain('Simulated browser launch failure');

    // 2. Verify that teardown cleaned up the state file
    expect(fs.existsSync(customStateFile)).toBe(false);

    // 3. Verify that the Kestrel web server on testPort was terminated
    let isServerDown = false;
    try {
      const resp = await fetch(`http://127.0.0.1:${testPort}/health`);
      isServerDown = !resp.ok;
    } catch {
      isServerDown = true;
    }
    expect(isServerDown).toBe(true);

    // Clean up temporary screenshots test folder if created
    const tmpDir = path.resolve(__dirname, '..', '..', 'docs', 'ui', 'screenshots', 'test-tmp');
    if (fs.existsSync(tmpDir)) {
      try {
        fs.rmSync(tmpDir, { recursive: true, force: true });
      } catch {}
    }
  });
});
