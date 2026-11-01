import { FullConfig } from '@playwright/test';
import {
  createInitialE2eState,
  runRunnerCommand,
  saveE2eState,
  spawnWebServer,
} from './e2e-env';

async function globalSetup(_config: FullConfig): Promise<void> {
  const state = createInitialE2eState();
  console.log(`[E2E Setup] Creating ephemeral database: ${state.dbName}`);

  const createRes = runRunnerCommand(
    ['create-e2e-db', state.dbName, state.ownershipToken],
    state
  );

  if (createRes.status !== 0) {
    throw new Error(
      `[E2E Setup] Failed to create/migrate/seed E2E database ${state.dbName} (exit ${createRes.status}):\n${createRes.stdout}\n${createRes.stderr}`
    );
  }
  console.log(`[E2E Setup] ${createRes.stdout.trim()}`);

  const urlObj = new URL(state.baseUrl);
  const port = parseInt(urlObj.port || '5188', 10);

  console.log(`[E2E Setup] Starting BetaGiaiPhong.Web on ${state.baseUrl}...`);
  const server = await spawnWebServer(state, port);
  state.webPid = server.process.pid;

  // Unref child process so globalSetup can finish while server stays alive for tests
  server.process.unref();

  saveE2eState(state);
  console.log(`[E2E Setup] BetaGiaiPhong.Web ready (PID: ${state.webPid}) on ${state.baseUrl}`);
}

export default globalSetup;
