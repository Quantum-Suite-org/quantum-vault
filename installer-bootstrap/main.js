'use strict';

const { app, BrowserWindow, ipcMain, dialog, shell } = require('electron');
const path = require('path');
const fs = require('fs');
const os = require('os');
const { spawn } = require('child_process');

const APP_VERSION = '0.1.2';
const APP_NAME = 'Quantum Vault';
const PAYLOAD_FILE = 'QuantumVault-Setup.exe';
const INSTALLED_EXE = 'QuantumVault.exe';

// Exit codes for silent mode (returned to the calling shell or winget)
const EXIT = {
  OK: 0,
  INSTALLER_FAILED: 1,
  EULA_NOT_ACCEPTED: 2,
  PAYLOAD_MISSING: 3,
  INVALID_DIR: 4,
  ALREADY_RUNNING: 5
};

let win = null;
let installProcess = null;

// ---------- Command line ----------
// Silent usage (run from an elevated shell, or let winget run it):
//   QuantumVault-Setup-0.1.2.exe /S /ACCEPTEULA [/DIR="C:\path\QuantumVault"] [/LAUNCH]
//   /S          install with no window
//   /ACCEPTEULA required for silent installs; confirms acceptance of the EULA
//   /DIR=       optional install folder (default: Program Files\Quantum-suite-org\QuantumVault)
//   /LAUNCH     start Quantum Vault after a successful install
function parseArgs(argv) {
  const out = { silent: false, accept: false, dir: null, launch: false };
  for (const arg of argv) {
    const a = arg.trim();
    const lower = a.toLowerCase();
    if (lower === '/s' || lower === '--silent') out.silent = true;
    else if (lower === '/accepteula') out.accept = true;
    else if (lower === '/launch') out.launch = true;
    else if (lower.startsWith('/dir=')) out.dir = a.slice(5).replace(/^"+|"+$/g, '');
  }
  return out;
}

const CLI = parseArgs(process.argv.slice(1));

// ---------- Paths ----------

function payloadPath() {
  const base = app.isPackaged ? process.resourcesPath : __dirname;
  return path.join(base, 'payload', PAYLOAD_FILE);
}

function defaultInstallDir() {
  const programFiles = process.env.ProgramFiles || 'C:\\Program Files';
  return path.join(programFiles, 'Quantum-suite-org', 'QuantumVault');
}

function logPath() {
  return path.join(os.tmpdir(), 'qv-setup-install.log');
}

function silentLogPath() {
  return path.join(os.tmpdir(), 'qv-setup-silent.log');
}

// ---------- Validation (shared by UI and silent mode) ----------

function validateDirPath(dir) {
  if (typeof dir !== 'string' || dir.trim().length === 0) {
    return { ok: false, reason: 'Enter a folder path.' };
  }
  if (!path.isAbsolute(dir)) {
    return { ok: false, reason: 'Use a full path, for example C:\\Program Files\\Quantum-suite-org\\QuantumVault.' };
  }
  if (/[<>"|?*]/.test(dir)) {
    return { ok: false, reason: 'The path contains characters that Windows does not allow.' };
  }
  const root = path.parse(dir).root;
  if (!fs.existsSync(root)) {
    return { ok: false, reason: `The drive ${root} does not exist.` };
  }
  return { ok: true };
}

// ---------- Installation engine (chains the silent Inno Setup package) ----------

function countLogLines(file) {
  try {
    const text = fs.readFileSync(file, 'utf8');
    const copied = (text.match(/Copying file/gi) || []).length;
    const created = (text.match(/Creating (directory|shortcut|registry)/gi) || []).length;
    return { copied, created, lines: text.split(/\r?\n/).length };
  } catch (_) {
    return { copied: 0, created: 0, lines: 0 };
  }
}

// Runs the payload silently and resolves with the outcome.
// emit(channel, data) is called with progress; in silent mode it is a no-op.
function runInstaller(installDir, emit) {
  const payload = payloadPath();
  if (!fs.existsSync(payload)) {
    return Promise.resolve({ ok: false, code: EXIT.PAYLOAD_MISSING, message: 'The installer payload is missing from this bootstrapper.' });
  }
  if (installProcess) {
    return Promise.resolve({ ok: false, code: EXIT.ALREADY_RUNNING, message: 'An installation is already running.' });
  }

  const log = logPath();
  try { fs.unlinkSync(log); } catch (_) { /* no previous log */ }

  const args = [
    '/VERYSILENT',
    '/SUPPRESSMSGBOXES',
    '/NORESTART',
    '/SP-',
    `/DIR=${installDir}`,
    `/LOG=${log}`
  ];

  emit('install:phase', { phase: 'prepare', label: 'Preparing the installation' });

  return new Promise((resolve) => {
    installProcess = spawn(payload, args, { windowsHide: true });

    let finished = false;
    const poll = setInterval(() => emit('install:progress', countLogLines(log)), 350);

    installProcess.on('error', (err) => {
      if (finished) return;
      finished = true;
      clearInterval(poll);
      installProcess = null;
      resolve({ ok: false, code: EXIT.INSTALLER_FAILED, message: `Could not start the installer: ${err.message}`, log });
    });

    installProcess.on('exit', (code) => {
      if (finished) return;
      finished = true;
      clearInterval(poll);
      installProcess = null;
      emit('install:progress', countLogLines(log));
      if (code === 0) {
        resolve({ ok: true, code: EXIT.OK, installDir, log });
      } else {
        resolve({
          ok: false,
          code: EXIT.INSTALLER_FAILED,
          message: `The installer finished with code ${code}. The log is at ${log}.`,
          log
        });
      }
    });
  });
}

function launchInstalled(installDir) {
  const exe = path.join(installDir, INSTALLED_EXE);
  if (!fs.existsSync(exe)) return false;
  const child = spawn(exe, [], { detached: true, stdio: 'ignore', cwd: installDir });
  child.unref();
  return true;
}

// ---------- Silent mode ----------

function runSilent() {
  const logFile = silentLogPath();
  const say = (msg) => {
    try { fs.appendFileSync(logFile, `${new Date().toISOString()}  ${msg}\n`); } catch (_) { /* log is best effort */ }
  };
  const finish = (code) => {
    say(`exit code ${code}`);
    app.exit(code);
  };

  say(`silent install started (version ${APP_VERSION})`);

  if (!CLI.accept) {
    say('refused: the EULA must be accepted with /ACCEPTEULA for silent installs');
    return finish(EXIT.EULA_NOT_ACCEPTED);
  }

  const installDir = CLI.dir || defaultInstallDir();
  const check = validateDirPath(installDir);
  if (!check.ok) {
    say(`refused: invalid install folder "${installDir}": ${check.reason}`);
    return finish(EXIT.INVALID_DIR);
  }

  say(`installing to ${installDir}`);
  runInstaller(installDir, () => { /* silent: no UI to update */ }).then((res) => {
    if (!res.ok) {
      say(`failed: ${res.message}`);
      return finish(res.code);
    }
    say('installation complete');
    if (CLI.launch) {
      const started = launchInstalled(installDir);
      say(started ? 'launched Quantum Vault' : 'could not find QuantumVault.exe to launch');
    }
    finish(EXIT.OK);
  });
}

// ---------- Interactive window ----------

function createWindow() {
  win = new BrowserWindow({
    width: 1040,
    height: 680,
    minWidth: 900,
    minHeight: 600,
    frame: false,
    resizable: true,
    backgroundColor: '#050008',
    show: false,
    title: `${APP_NAME} Setup`,
    icon: path.join(__dirname, 'assets', 'icon.ico'),
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    }
  });

  win.loadFile(path.join(__dirname, 'renderer', 'index.html'));
  win.once('ready-to-show', () => win.show());
  win.on('closed', () => {
    win = null;
    if (installProcess) {
      try { installProcess.kill(); } catch (_) { /* already exited */ }
    }
  });
}

// ---------- IPC: information ----------

ipcMain.handle('setup:info', () => ({
  appName: APP_NAME,
  version: APP_VERSION,
  defaultDir: defaultInstallDir(),
  payloadFound: fs.existsSync(payloadPath()),
  platform: process.platform,
  arch: process.arch
}));

ipcMain.handle('setup:eula', () => {
  const file = path.join(__dirname, 'renderer', 'assets', 'eula.txt');
  try {
    return fs.readFileSync(file, 'utf8');
  } catch (_) {
    return 'The End User License Agreement could not be loaded. Installation cannot continue.';
  }
});

// ---------- IPC: window controls ----------

ipcMain.on('window:minimise', () => win && win.minimize());
ipcMain.on('window:close', () => win && win.close());

// ---------- IPC: choosing a folder ----------

ipcMain.handle('setup:browse', async (_event, currentDir) => {
  const result = await dialog.showOpenDialog(win, {
    title: 'Choose where to install Quantum Vault',
    defaultPath: currentDir || defaultInstallDir(),
    properties: ['openDirectory', 'createDirectory']
  });
  if (result.canceled || result.filePaths.length === 0) return null;
  return path.join(result.filePaths[0], 'QuantumVault');
});

ipcMain.handle('setup:validate-dir', (_event, dir) => validateDirPath(dir));

// ---------- IPC: installation ----------

ipcMain.handle('setup:install', async (_event, installDir) => {
  const send = (channel, data) => {
    if (win && !win.isDestroyed()) win.webContents.send(channel, data);
  };
  return runInstaller(installDir, send);
});

ipcMain.handle('setup:launch', (_event, installDir) => {
  if (!launchInstalled(installDir)) {
    return { ok: false, message: 'The installed application could not be found.' };
  }
  return { ok: true };
});

ipcMain.handle('setup:open-log', () => {
  const log = logPath();
  if (fs.existsSync(log)) shell.showItemInFolder(log);
  return fs.existsSync(log);
});

ipcMain.handle('setup:open-link', (_event, url) => {
  if (typeof url === 'string' && /^https:\/\/github\.com\//.test(url)) {
    shell.openExternal(url);
    return true;
  }
  return false;
});

// ---------- App lifecycle ----------

app.whenReady().then(() => {
  if (CLI.silent) {
    runSilent();
    return;
  }
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (CLI.silent) return;
  if (process.platform !== 'darwin') app.quit();
});
