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

export function loadE2eState(): E2eState {
  if (!fs.existsSync(STATE_FILE)) {
    throw new Error(`E2E state file not found at ${STATE_FILE}. Ensure global-setup ran.`);
  }
  return JSON.parse(fs.readFileSync(STATE_FILE, 'utf-8')) as E2eState;
}

export function saveE2eState(state: E2eState): void {
  const dir = path.dirname(STATE_FILE);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2), 'utf-8');
}

export function buildConnectionString(masterConn: string, dbName: string): string {
  // Replace Database=... or Initial Catalog=... in masterConn or append Database=dbName
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

export function runRunnerCommand(args: string[], state: E2eState, extraEnv?: Record<string, string>): { status: number; stdout: string; stderr: string } {
  const runnerDll = path.join(ROOT_DIR, 'src', 'BetaGiaiPhong.Runner', 'bin', 'Release', 'net10.0', 'BetaGiaiPhong.Runner.dll');
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
    stdout: res.stdout || '',
    stderr: res.stderr || '',
  };
}

export async function waitForHealth(baseUrl: string, timeoutMs = 30_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  const healthUrl = `${baseUrl.replace(/\/$/, '')}/health`;
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
  extraEnv?: Record<string, string>
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

  try {
    await waitForHealth(baseUrl, 30_000);
  } catch (err) {
    child.kill();
    throw new Error(`Failed to start Web server on ${baseUrl}: ${err}\nLogs:\n${startupLogs}`);
  }

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

  return { process: child, baseUrl, stop };
}

export function createInitialE2eState(): E2eState {
  const runSuffix = `${Date.now().toString(36)}_${crypto.randomBytes(3).toString('hex')}`;
  const dbName = `BetaGiaiPhong_E2E_${runSuffix}`;
  const ownershipToken = crypto.randomBytes(16).toString('hex');
  const masterConnection =
    process.env.BGP_TEST_SQL_MASTER_CONNECTION ||
    'Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;';
  const connectionString = buildConnectionString(masterConnection, dbName);
  const baseUrl = process.env.BGP_E2E_BASE_URL || 'http://127.0.0.1:5188';
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
