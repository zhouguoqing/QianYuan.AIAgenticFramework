import { app, BrowserWindow, ipcMain, shell } from 'electron';
import log from 'electron-log/main';
import { startApi } from './apiProcess.js';
import { registerFileSystemIpc } from './fileSystem.js';
import { preloadPath, webDistPath } from './paths.js';
import { startStaticServer } from './staticServer.js';
let apiRuntime = null;
let staticRuntime = null;
const apiPort = Number(process.env.WORKPARTNER_API_PORT ?? 5050);
const rendererPort = Number(process.env.WORKPARTNER_RENDERER_PORT ?? 5180);
async function createWindow() {
    apiRuntime = await startApi(apiPort);
    const rendererUrl = await resolveRendererUrl();
    const win = new BrowserWindow({
        width: 1440,
        height: 920,
        minWidth: 1120,
        minHeight: 720,
        title: 'WorkPartner',
        webPreferences: {
            preload: preloadPath(),
            contextIsolation: true,
            nodeIntegration: false,
            sandbox: false,
            additionalArguments: [`--workpartner-api-url=${apiRuntime.url}`],
        },
    });
    win.webContents.setWindowOpenHandler(({ url }) => {
        void shell.openExternal(url);
        return { action: 'deny' };
    });
    await win.loadURL(rendererUrl);
}
async function resolveRendererUrl() {
    if (process.env.WORKPARTNER_RENDERER_URL)
        return process.env.WORKPARTNER_RENDERER_URL;
    if (!app.isPackaged)
        return 'http://127.0.0.1:5173';
    staticRuntime = await startStaticServer(webDistPath(), rendererPort);
    return staticRuntime.url;
}
app.whenReady().then(async () => {
    log.initialize();
    ipcMain.handle('workpartner:getRuntime', () => ({
        apiBaseUrl: apiRuntime?.url ?? `http://127.0.0.1:${apiPort}`,
        platform: process.platform,
        version: app.getVersion(),
    }));
    registerFileSystemIpc();
    await createWindow();
    app.on('activate', () => {
        if (BrowserWindow.getAllWindows().length === 0)
            void createWindow();
    });
}).catch(err => {
    log.error('Failed to start WorkPartner desktop', err);
    app.quit();
});
app.on('window-all-closed', () => {
    if (process.platform !== 'darwin')
        app.quit();
});
app.on('before-quit', async (event) => {
    event.preventDefault();
    await staticRuntime?.stop();
    await apiRuntime?.stop();
    staticRuntime = null;
    apiRuntime = null;
    app.exit(0);
});
