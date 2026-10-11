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

let win = null;
let installProcess = null;

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
  } catch (err) {
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

ipcMain.handle('setup:validate-dir', (_event, dir) => {
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
});

// ---------- IPC: installation (chains the silent Inno Setup package) ----------

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

ipcMain.handle('setup:install', async (event, installDir) => {
  const payload = payloadPath();
  if (!fs.existsSync(payload)) {
    return { ok: false, code: -1, message: 'The installer payload is missing from this bootstrapper.' };
  }
  if (installProcess) {
    return { ok: false, code: -2, message: 'An installation is already running.' };
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

  const send = (channel, payloadData) => {
    if (win && !win.isDestroyed()) win.webContents.send(channel, payloadData);
  };

  send('install:phase', { phase: 'prepare', label: 'Preparing the installation' });

  return new Promise((resolve) => {
    installProcess = spawn(payload, args, { windowsHide: true });

    let finished = false;
    const poll = setInterval(() => {
      const stats = countLogLines(log);
      send('install:progress', stats);
    }, 350);

    installProcess.on('error', (err) => {
      finished = true;
      clearInterval(poll);
      installProcess = null;
      resolve({ ok: false, code: -3, message: `Could not start the installer: ${err.message}` });
    });

    installProcess.on('exit', (code) => {
      if (finished) return;
      finished = true;
      clearInterval(poll);
      installProcess = null;
      const stats = countLogLines(log);
      send('install:progress', stats);
      if (code === 0) {
        resolve({ ok: true, code, installDir, log });
      } else {
        resolve({
          ok: false,
          code,
          message: `The installer finished with code ${code}. The log is at ${log}.`,
          log
        });
      }
    });
  });
});

ipcMain.handle('setup:launch', (_event, installDir) => {
  const exe = path.join(installDir, INSTALLED_EXE);
  if (!fs.existsSync(exe)) {
    return { ok: false, message: 'The installed application could not be found.' };
  }
  const child = spawn(exe, [], { detached: true, stdio: 'ignore', cwd: installDir });
  child.unref();
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
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
