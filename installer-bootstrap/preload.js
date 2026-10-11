'use strict';

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('qv', {
  info: () => ipcRenderer.invoke('setup:info'),
  eula: () => ipcRenderer.invoke('setup:eula'),
  browse: (currentDir) => ipcRenderer.invoke('setup:browse', currentDir),
  validateDir: (dir) => ipcRenderer.invoke('setup:validate-dir', dir),
  install: (dir) => ipcRenderer.invoke('setup:install', dir),
  launch: (dir) => ipcRenderer.invoke('setup:launch', dir),
  openLog: () => ipcRenderer.invoke('setup:open-log'),
  openLink: (url) => ipcRenderer.invoke('setup:open-link', url),
  minimise: () => ipcRenderer.send('window:minimise'),
  close: () => ipcRenderer.send('window:close'),
  onPhase: (callback) => ipcRenderer.on('install:phase', (_e, data) => callback(data)),
  onProgress: (callback) => ipcRenderer.on('install:progress', (_e, data) => callback(data))
});
