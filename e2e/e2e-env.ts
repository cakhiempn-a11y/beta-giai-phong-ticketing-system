import { spawn, spawnSync, ChildProcess } from 'child_process';
import * as crypto from 'crypto';
import * as fs from 'fs';
import * as path from 'path';

export interface E2eState {
  dbName: string;
  ownershipToken: string;
  connectionString: string;
  masterConnection: string;
  baseUrl: string;
  qrAesKeyB64: string;
  mockHmacKeyB64: string;
  demoPassword: string;
  webPid?: number;
}

const ROOT_DIR = path.resolve(__dirname, '..');
const STATE_FILE = path.join(__dirname, '.tmp', 'e2e-state.json');

export function getRootDir(): string {
  return ROOT_DIR;
}

export function getStateFilePath(): string {
  return STATE_FILE;
}

export function sanitizeTextForLog(text: string, state?: Partial<E2eState>): string {
  if (!text) return '';
  let out = text;
  if (state?.qrAesKeyB64) out = out.split(state.qrAesKeyB64).join('[REDACTED_QR_AES_KEY]');
  if (state?.mockHmacKeyB64) out = out.split(state.mockHmacKeyB64).join('[REDACTED_HMAC_KEY]');
  if (state?.demoPassword) out = out.split(state.demoPassword).join('[REDACTED_DEMO_PWD]');
  out = out.replace(/Password\s*=\s*[^;]+/gi, 'Password=[REDACTED]');
  return out;
}

export function loadE2eState(customPath = STATE_FILE): E2eState {
  if (!fs.existsSync(customPath)) {
    throw new Error(`E2E state file not found at ${customPath}. Ensure global-setup ran.`);
  }
  return JSON.parse(fs.readFileSync(customPath, 'utf-8')) as E2eState;
}

export function saveE2eState(state: E2eState, customPath = STATE_FILE): void {
  const dir = path.dirname(customPath);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  fs.writeFileSync(customPath, JSON.stringify(state, null, 2), { encoding: 'utf-8', mode: 0o600 });
}

export function removeE2eStateFile(customPath = STATE_FILE): void {
  if (fs.existsSync(customPath)) {
    fs.unlinkSync(customPath);
  }
}

export function buildConnectionString(masterConn: string, dbName: string): string {
  let conn = masterConn;
  if (/Database\s*=\s*[^;]+/i.test(conn)) {
    conn = conn.replace(/Database\s*=\s*[^;]+/i, `Database=${dbName}`);
  } else if (/Initial Catalog\s*=\s*[^;]+/i.test(conn)) {
    conn = conn.replace(/Initial Catalog\s*=\s*[^;]+/i, `Initial Catalog=${dbName}`);
  } else {
    conn = conn.endsWith(';') ? `${conn}Database=${dbName};` : `${conn};Database=${dbName};`;
  }
  return conn;
}

export function runRunnerCommand(
  args: string[],
  state: E2eState,
  extraEnv?: Record<string, string>
): { status: number; stdout: string; stderr: string } {
  const runnerDll = path.join(
    ROOT_DIR,
    'src',
    'BetaGiaiPhong.Runner',
    'bin',
    'Release',
    'net10.0',
    'BetaGiaiPhong.Runner.dll'
  );
  const cmdArgs = fs.existsSync(runnerDll)
    ? [runnerDll, ...args]
    : ['run', '--project', path.join(ROOT_DIR, 'src', 'BetaGiaiPhong.Runner'), '-c', 'Release', '--no-build', '--', ...args];

  const env: NodeJS.ProcessEnv = {
    ...process.env,
    BGP_TEST_SQL_MASTER_CONNECTION: state.masterConnection,
    ConnectionStrings__BetaDb: state.connectionString,
    BGP_QR_AES_KEY_B64: state.qrAesKeyB64,
    BGP_MOCK_HMAC_KEY_B64: state.mockHmacKeyB64,
    BGP_ALLOW_DEMO_SEED: 'true',
    BGP_DEMO_PASSWORD: state.demoPassword,
    ...extraEnv,
  };

  const res = spawnSync('dotnet', cmdArgs, {
    cwd: ROOT_DIR,
    env,
    encoding: 'utf-8',
    timeout: 60_000,
  });

  return {
    status: res.status ?? 1,
    stdout: sanitizeTextForLog(res.stdout || '', state),
    stderr: sanitizeTextForLog(res.stderr || '', state),
  };
}

export async function waitForHealth(baseUrl: string, timeoutMs = 30_000, healthPath = '/health'): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  const healthUrl = `${baseUrl.replace(/\/$/, '')}${healthPath}`;
  while (Date.now() < deadline) {
    try {
      const resp = await fetch(healthUrl);
      if (resp.ok) {
        return;
      }
    } catch {
      // Server not ready yet
    }
    await new Promise((r) => setTimeout(r, 400));
  }
  throw new Error(`Timed out waiting for Web server health check at ${healthUrl}`);
}

export async function spawnWebServer(
  state: E2eState,
  port: number,
  extraEnv?: Record<string, string>,
  healthOptions?: { timeoutMs?: number; healthPath?: string }
): Promise<{ process: ChildProcess; baseUrl: string; stop: () => Promise<void> }> {
  const baseUrl = `http://127.0.0.1:${port}`;
  const webDir = path.join(ROOT_DIR, 'src', 'BetaGiaiPhong.Web');
  const webDll = path.join(webDir, 'bin', 'Release', 'net10.0', 'BetaGiaiPhong.Web.dll');

  const cmdArgs = fs.existsSync(webDll)
    ? [webDll, '--urls', baseUrl]
    : ['run', '--project', webDir, '-c', 'Release', '--no-build', '--no-launch-profile', '--urls', baseUrl];

  const env: NodeJS.ProcessEnv = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: baseUrl,
    ConnectionStrings__BetaDb: state.connectionString,
    BGP_QR_AES_KEY_B64: state.qrAesKeyB64,
    BGP_MOCK_HMAC_KEY_B64: state.mockHmacKeyB64,
    BGP_ENABLE_MOCK_PAYMENT_SIMULATOR: 'true',
    BGP_ENABLE_EXPIRY_WORKER: 'true',
    BGP_EXPIRY_INTERVAL_SECONDS: '2',
    BGP_LOGIN_RATE_LIMIT_PERMITS: '500',
    ...extraEnv,
  };

  const child = spawn('dotnet', cmdArgs, {
    cwd: webDir,
    env,
    stdio: ['ignore', 'pipe', 'pipe'],
  });

  let startupLogs = '';
  child.stdout?.on('data', (chunk) => {
    startupLogs += chunk.toString();
  });
  child.stderr?.on('data', (chunk) => {
    startupLogs += chunk.toString();
  });

  const stop = async () => {
    if (child.killed || child.exitCode !== null) return;
    await new Promise<void>((resolve) => {
      const timer = setTimeout(() => {
        try {
          child.kill('SIGKILL');
        } catch {}
        resolve();
      }, 5000);
      child.once('exit', () => {
        clearTimeout(timer);
        resolve();
      });
      child.kill('SIGTERM');
    });
  };

  try {
    await waitForHealth(
      baseUrl,
      healthOptions?.timeoutMs ?? 30_000,
      healthOptions?.healthPath ?? '/health'
    );
  } catch (err) {
    await stop();
    const safeLogs = sanitizeTextForLog(startupLogs, state);
    throw new Error(`Failed to start Web server on ${baseUrl}: ${err}\nLogs:\n${safeLogs}`);
  }

  return { process: child, baseUrl, stop };
}

export function createInitialE2eState(customSuffix?: string, customPort?: number): E2eState {
  const runSuffix = customSuffix || `${Date.now().toString(36)}_${crypto.randomBytes(3).toString('hex')}`;
  const dbName = `BetaGiaiPhong_E2E_${runSuffix}`;
  const ownershipToken = crypto.randomBytes(16).toString('hex');
  const masterConnection =
    process.env.BGP_TEST_SQL_MASTER_CONNECTION ||
    'Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;';
  const connectionString = buildConnectionString(masterConnection, dbName);
  const baseUrl = customPort
    ? `http://127.0.0.1:${customPort}`
    : process.env.BGP_E2E_BASE_URL || 'http://127.0.0.1:5188';
  const qrAesKeyB64 = crypto.randomBytes(32).toString('base64');
  const mockHmacKeyB64 = crypto.randomBytes(32).toString('base64');
  const demoPassword = process.env.BGP_DEMO_PASSWORD || 'DemoPassword123!';

  return {
    dbName,
    ownershipToken,
    connectionString,
    masterConnection,
    baseUrl,
    qrAesKeyB64,
    mockHmacKeyB64,
    demoPassword,
  };
}

export async function runGlobalSetupLifecycle(
  state: E2eState,
  options?: {
    stateFilePath?: string;
    healthTimeoutMs?: number;
    healthPath?: string;
  }
): Promise<void> {
  const stateFilePath = options?.stateFilePath ?? STATE_FILE;
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

  // Phase 0.2: Persist ownership/state immediately after database creation BEFORE starting Web server
  saveE2eState(state, stateFilePath);

  const urlObj = new URL(state.baseUrl);
  const port = parseInt(urlObj.port || '5188', 10);

  try {
    console.log(`[E2E Setup] Starting BetaGiaiPhong.Web on ${state.baseUrl}...`);
    const server = await spawnWebServer(state, port, undefined, {
      timeoutMs: options?.healthTimeoutMs,
      healthPath: options?.healthPath,
    });
    state.webPid = server.process.pid;
    server.process.unref();
    saveE2eState(state, stateFilePath);
    console.log(`[E2E Setup] BetaGiaiPhong.Web ready (PID: ${state.webPid}) on ${state.baseUrl}`);
  } catch (setupErr) {
    console.error(
      `[E2E Setup] Web startup failed after creating ${state.dbName}. Cleaning up database...`
    );
    const dropRes = runRunnerCommand(
      ['drop-e2e-db', state.dbName, state.ownershipToken],
      state
    );
    if (dropRes.status === 0) {
      removeE2eStateFile(stateFilePath);
      console.log(`[E2E Setup] Cleaned up database ${state.dbName} after setup failure.`);
    } else {
      console.error(
        `[E2E Setup] Failed to drop database ${state.dbName} during setup failure cleanup:\n${dropRes.stderr}`
      );
    }
    throw setupErr;
  }
}

export async function runGlobalTeardownLifecycle(
  stateFilePath = STATE_FILE,
  options?: { forceAutoCleanup?: boolean }
): Promise<void> {
  if (!fs.existsSync(stateFilePath)) {
    return;
  }

  const state = loadE2eState(stateFilePath);
  let integrityError: Error | null = null;
  let cleanupError: Error | null = null;

  try {
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
    if (verifyRes.stdout.trim()) {
      console.log(verifyRes.stdout.trim());
    }
    const passedMarkerPresent = verifyRes.stdout.includes('INTEGRITY PASSED');
    if (verifyRes.status !== 0 || !passedMarkerPresent) {
      const detail = sanitizeTextForLog(`${verifyRes.stdout}\n${verifyRes.stderr}`.trim(), state);
      console.error(`[E2E Teardown] Integrity verification FAILED (exit ${verifyRes.status}):\n${detail}`);
      integrityError = new Error(
        `[E2E Quality Gate] Runner verify failed on ${state.dbName} (exit ${verifyRes.status}, passedMarker=${passedMarkerPresent}):\n${detail}`
      );
    }
  } finally {
    const autoCleanupEnv = process.env.BGP_TEST_AUTO_CLEANUP_DB;
    const shouldSkipCleanup =
      options?.forceAutoCleanup !== true &&
      autoCleanupEnv &&
      autoCleanupEnv.toLowerCase() === 'false';

    if (shouldSkipCleanup) {
      console.log(`[E2E Teardown] BGP_TEST_AUTO_CLEANUP_DB=false — preserving database ${state.dbName}`);
    } else {
      console.log(`[E2E Teardown] Dropping ephemeral E2E database ${state.dbName}...`);
      const dropRes = runRunnerCommand(
        ['drop-e2e-db', state.dbName, state.ownershipToken],
        state
      );
      if (dropRes.status !== 0) {
        const dropErrText = sanitizeTextForLog(`${dropRes.stdout}\n${dropRes.stderr}`.trim(), state);
        console.error(`[E2E Teardown] Failed to drop E2E database ${state.dbName}:\n${dropErrText}`);
        cleanupError = new Error(
          `[E2E Teardown] Database cleanup failed for ${state.dbName} (exit ${dropRes.status}):\n${dropErrText}`
        );
      } else {
        console.log(`[E2E Teardown] ${dropRes.stdout.trim()}`);
        removeE2eStateFile(stateFilePath);
      }
    }
  }

  if (integrityError && cleanupError) {
    throw new Error(`${integrityError.message}\nAdditionally, cleanup failed:\n${cleanupError.message}`);
  }
  if (integrityError) {
    throw integrityError;
  }
  if (cleanupError) {
    throw cleanupError;
  }
}
