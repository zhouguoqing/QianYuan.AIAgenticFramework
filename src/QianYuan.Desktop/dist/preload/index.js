import { contextBridge, ipcRenderer } from 'electron';
const apiArg = process.argv.find(arg => arg.startsWith('--workpartner-api-url='));
const apiBaseUrl = apiArg?.split('=')[1] ?? 'http://127.0.0.1:5050';
contextBridge.exposeInMainWorld('workpartner', {
    apiBaseUrl,
    platform: process.platform,
    version: process.versions.electron,
    getRuntime: async () => ipcRenderer.invoke('workpartner:getRuntime'),
});
