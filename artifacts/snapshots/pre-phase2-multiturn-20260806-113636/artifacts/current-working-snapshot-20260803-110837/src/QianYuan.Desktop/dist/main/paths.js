import { app } from 'electron';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const currentFile = fileURLToPath(import.meta.url);
const currentDir = path.dirname(currentFile);
export function repoRoot() {
    if (app.isPackaged)
        return process.resourcesPath;
    return path.resolve(currentDir, '../../../..');
}
export function apiProjectPath() {
    return path.join(repoRoot(), 'src', 'QianYuan.Api', 'QianYuan.Api.csproj');
}
export function packagedApiExecutable() {
    const fileName = process.platform === 'win32' ? 'QianYuan.Api.exe' : 'QianYuan.Api';
    return path.join(process.resourcesPath, 'api', fileName);
}
export function webDistPath() {
    if (app.isPackaged)
        return path.join(process.resourcesPath, 'web');
    return path.join(repoRoot(), 'src', 'QianYuan.Web', 'dist');
}
export function preloadPath() {
    return path.join(currentDir, '..', 'preload', 'index.js');
}
