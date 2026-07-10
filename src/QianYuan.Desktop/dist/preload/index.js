import { contextBridge, ipcRenderer } from 'electron';
import { desktopFileSystemChannels, } from '../common/fileSystem.js';
const apiArg = process.argv.find(arg => arg.startsWith('--workpartner-api-url='));
const apiBaseUrl = apiArg?.split('=')[1] ?? 'http://127.0.0.1:5050';
contextBridge.exposeInMainWorld('workpartner', {
    apiBaseUrl,
    platform: process.platform,
    version: process.versions.electron,
    getRuntime: async () => ipcRenderer.invoke('workpartner:getRuntime'),
    fileSystem: {
        getRoots: async () => ipcRenderer.invoke(desktopFileSystemChannels.getRoots),
        selectDirectory: async () => ipcRenderer.invoke(desktopFileSystemChannels.selectDirectory),
        selectFiles: async () => ipcRenderer.invoke(desktopFileSystemChannels.selectFiles),
        stat: async (target) => ipcRenderer.invoke(desktopFileSystemChannels.stat, target),
        listDirectory: async (request) => ipcRenderer.invoke(desktopFileSystemChannels.listDirectory, request),
        readTextFile: async (request) => ipcRenderer.invoke(desktopFileSystemChannels.readTextFile, request),
        writeTextFile: async (request) => ipcRenderer.invoke(desktopFileSystemChannels.writeTextFile, request),
        createDirectory: async (target) => ipcRenderer.invoke(desktopFileSystemChannels.createDirectory, target),
    },
});
