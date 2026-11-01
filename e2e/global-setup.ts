import { FullConfig } from '@playwright/test';
import { createInitialE2eState, runGlobalSetupLifecycle } from './e2e-env';

async function globalSetup(_config: FullConfig): Promise<void> {
  const state = createInitialE2eState();
  await runGlobalSetupLifecycle(state);
}

export default globalSetup;
