import { FullConfig } from '@playwright/test';
import { getStateFilePath, runGlobalTeardownLifecycle } from './e2e-env';

async function globalTeardown(_config: FullConfig): Promise<void> {
  await runGlobalTeardownLifecycle(getStateFilePath());
}

export default globalTeardown;
