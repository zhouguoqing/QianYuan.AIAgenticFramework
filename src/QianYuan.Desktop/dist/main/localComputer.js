import { app, ipcMain } from 'electron';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { localComputerChannels, } from '../common/localComputer.js';
import { repoRoot } from './paths.js';
const maxOutputBytes = 512 * 1024;
const defaultTimeoutMs = 120_000;
const maxTimeoutMs = 15 * 60_000;
const commands = new Map();
export function registerLocalComputerIpc() {
    ipcMain.handle(localComputerChannels.startCommand, async (_event, request) => startCommand(request));
    ipcMain.handle(localComputerChannels.getCommand, async (_event, id) => getCommand(id));
    ipcMain.handle(localComputerChannels.listCommands, async () => listCommands());
    ipcMain.handle(localComputerChannels.cancelCommand, async (_event, id) => cancelCommand(id));
}
async function startCommand(request) {
    const command = request.command?.trim();
    if (!command)
        throw new Error('command is required');
    const args = request.args?.filter(Boolean) ?? [];
    const cwd = resolveCwd(request.cwd);
    const timeoutMs = normalizeTimeout(request.timeoutMs);
    const id = randomUUID();
    const startedAt = new Date().toISOString();
    const proc = spawn(command, args, {
        cwd,
        env: {
            ...process.env,
            ...request.env,
        },
        stdio: 'pipe',
        shell: false,
        windowsHide: true,
    });
    const runtime = {
        id,
        pid: proc.pid,
        command,
        args,
        cwd,
        status: 'running',
        startedAt,
        stdout: '',
        stderr: '',
    };
    const entry = { runtime, process: proc };
    commands.set(id, entry);
    proc.stdout.on('data', chunk => {
        runtime.stdout = appendBounded(runtime.stdout, chunk.toString('utf8'));
    });
    proc.stderr.on('data', chunk => {
        runtime.stderr = appendBounded(runtime.stderr, chunk.toString('utf8'));
    });
    proc.on('error', err => {
        runtime.status = 'failed';
        runtime.error = err.message;
        runtime.finishedAt = new Date().toISOString();
        clearTimeout(entry.timeout);
    });
    proc.on('exit', (code, signal) => {
        if (runtime.status === 'running') {
            runtime.status = code === 0 ? 'completed' : 'failed';
        }
        runtime.exitCode = code;
        runtime.signal = signal;
        runtime.finishedAt = new Date().toISOString();
        clearTimeout(entry.timeout);
    });
    entry.timeout = setTimeout(() => {
        if (runtime.status !== 'running')
            return;
        runtime.status = 'timeout';
        runtime.error = `Command timed out after ${timeoutMs}ms`;
        runtime.finishedAt = new Date().toISOString();
        try {
            proc.kill('SIGTERM');
        }
        catch {
            // ignore
        }
        setTimeout(() => {
            if (!proc.killed) {
                try {
                    proc.kill('SIGKILL');
                }
                catch {
                    // ignore
                }
            }
        }, 1_000).unref();
    }, timeoutMs);
    return runtime;
}
function getCommand(id) {
    return commands.get(id)?.runtime ?? null;
}
function listCommands() {
    return Array.from(commands.values())
        .map(item => item.runtime)
        .sort((left, right) => right.startedAt.localeCompare(left.startedAt));
}
function cancelCommand(id) {
    const entry = commands.get(id);
    if (!entry)
        return null;
    const { runtime, process: proc } = entry;
    if (runtime.status === 'running') {
        runtime.status = 'canceled';
        runtime.finishedAt = new Date().toISOString();
        runtime.error = 'Canceled by user';
        try {
            proc.kill('SIGTERM');
        }
        catch {
            // ignore
        }
    }
    return runtime;
}
function normalizeTimeout(timeoutMs) {
    const candidate = timeoutMs ?? defaultTimeoutMs;
    return Math.min(Math.max(candidate, 1_000), maxTimeoutMs);
}
function resolveCwd(input) {
    const fallback = repoRoot();
    const base = input ? path.resolve(expandHome(input)) : fallback;
    if (!isAllowedCwd(base))
        throw new Error(`cwd is not allowed: ${base}`);
    return base;
}
function expandHome(input) {
    if (input === '~')
        return app.getPath('home');
    if (input.startsWith(`~${path.sep}`))
        return path.join(app.getPath('home'), input.slice(2));
    return input;
}
function isAllowedCwd(candidate) {
    const roots = [repoRoot(), app.getPath('desktop'), app.getPath('documents'), app.getPath('home')]
        .map(item => path.resolve(item));
    return roots.some(root => isInside(root, candidate));
}
function isInside(rootPath, filePath) {
    const relative = path.relative(rootPath, path.resolve(filePath));
    return relative === '' || (!!relative && !relative.startsWith('..') && !path.isAbsolute(relative));
}
function appendBounded(current, next) {
    const combined = `${current}${next}`;
    if (Buffer.byteLength(combined, 'utf8') <= maxOutputBytes)
        return combined;
    const bytes = Buffer.from(combined, 'utf8');
    return bytes.subarray(bytes.length - maxOutputBytes).toString('utf8');
}
