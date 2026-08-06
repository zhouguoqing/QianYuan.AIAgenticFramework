import { app, dialog, ipcMain } from 'electron'
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import fs from 'node:fs/promises'
import path from 'node:path'
import {
  desktopFileSystemChannels,
  DesktopDirectoryListing,
  DesktopFileEntry,
  DesktopFileKind,
  DesktopFileRoot,
  DesktopFileTarget,
  DesktopListDirectoryRequest,
  DesktopReadTextFileRequest,
  DesktopReadTextFileResult,
  DesktopWriteTextFileRequest,
} from '../common/fileSystem.js'
import { repoRoot } from './paths.js'

const grantsFileName = 'file-system-roots.json'
const defaultMaxEntries = 500
const defaultMaxReadBytes = 10 * 1024 * 1024

interface ResolvedTarget {
  root: DesktopFileRoot
  path: string
}

export function registerFileSystemIpc() {
  ipcMain.handle(desktopFileSystemChannels.getRoots, () => getRoots())
  ipcMain.handle(desktopFileSystemChannels.selectDirectory, async () => selectDirectory())
  ipcMain.handle(desktopFileSystemChannels.selectFiles, async () => selectFiles())
  ipcMain.handle(desktopFileSystemChannels.stat, async (_event, target: DesktopFileTarget) => stat(target))
  ipcMain.handle(desktopFileSystemChannels.listDirectory, async (_event, request: DesktopListDirectoryRequest) => listDirectory(request))
  ipcMain.handle(desktopFileSystemChannels.readTextFile, async (_event, request: DesktopReadTextFileRequest) => readTextFile(request))
  ipcMain.handle(desktopFileSystemChannels.writeTextFile, async (_event, request: DesktopWriteTextFileRequest) => writeTextFile(request))
  ipcMain.handle(desktopFileSystemChannels.createDirectory, async (_event, target: DesktopFileTarget) => createDirectory(target))
}

function getRoots(): DesktopFileRoot[] {
  return [...builtinRoots(), ...loadSelectedRoots()]
}

async function selectDirectory(): Promise<DesktopFileRoot | null> {
  const result = await dialog.showOpenDialog({
    title: '选择本地工作区',
    properties: ['openDirectory', 'createDirectory'],
  })
  if (result.canceled || result.filePaths.length === 0) return null
  return grantRoot(result.filePaths[0])
}

async function selectFiles(): Promise<DesktopFileEntry[]> {
  const result = await dialog.showOpenDialog({
    title: '选择本地文件',
    properties: ['openFile', 'multiSelections'],
  })
  if (result.canceled || result.filePaths.length === 0) return []

  const grantedParents = new Map<string, DesktopFileRoot>()
  for (const filePath of result.filePaths) {
    const parent = path.dirname(filePath)
    grantedParents.set(parent, await grantRoot(parent))
  }

  return Promise.all(result.filePaths.map(filePath => {
    const root = grantedParents.get(path.dirname(filePath))
    if (!root) throw new Error(`No granted root for ${filePath}`)
    return toEntry(root, filePath)
  }))
}

async function stat(target: DesktopFileTarget): Promise<DesktopFileEntry> {
  const resolved = resolveTarget(target)
  return toEntry(resolved.root, resolved.path)
}

async function listDirectory(request: DesktopListDirectoryRequest): Promise<DesktopDirectoryListing> {
  const resolved = resolveTarget(request)
  const directory = await toEntry(resolved.root, resolved.path)
  if (directory.kind !== 'directory') throw new Error(`Not a directory: ${resolved.path}`)

  const maxEntries = Math.max(1, Math.min(request.maxEntries ?? defaultMaxEntries, 2_000))
  const dirents = await fs.readdir(resolved.path, { withFileTypes: true })
  const entries = await Promise.all(dirents
    .sort((left, right) => compareDirents(left, right))
    .slice(0, maxEntries)
    .map(dirent => toEntry(resolved.root, path.join(resolved.path, dirent.name))))

  return { root: resolved.root, directory, entries }
}

async function readTextFile(request: DesktopReadTextFileRequest): Promise<DesktopReadTextFileResult> {
  const resolved = resolveTarget(request)
  const file = await toEntry(resolved.root, resolved.path)
  if (file.kind !== 'file') throw new Error(`Not a file: ${resolved.path}`)

  const maxBytes = Math.max(1, request.maxBytes ?? defaultMaxReadBytes)
  if (file.size > maxBytes) throw new Error(`File is ${file.size} bytes, above limit ${maxBytes}`)

  const content = await fs.readFile(resolved.path, 'utf8')
  return { root: resolved.root, file, content }
}

async function writeTextFile(request: DesktopWriteTextFileRequest): Promise<DesktopFileEntry> {
  const resolved = resolveTarget(request, true)
  if (request.createDirectory !== false) await fs.mkdir(path.dirname(resolved.path), { recursive: true })

  const flag = request.overwrite === false ? 'wx' : 'w'
  await fs.writeFile(resolved.path, request.content, { encoding: 'utf8', flag })
  return toEntry(resolved.root, resolved.path)
}

async function createDirectory(target: DesktopFileTarget): Promise<DesktopFileEntry> {
  const resolved = resolveTarget(target, true)
  await fs.mkdir(resolved.path, { recursive: true })
  return toEntry(resolved.root, resolved.path)
}

function resolveTarget(target: DesktopFileTarget, forWrite = false): ResolvedTarget {
  if (target.rootId) {
    const root = getRoots().find(item => item.id === target.rootId)
    if (!root) throw new Error(`Unknown file root: ${target.rootId}`)
    if (forWrite && !root.writable) throw new Error(`File root is read-only: ${root.label}`)
    const relativePath = target.relativePath ?? '.'
    if (path.isAbsolute(relativePath)) throw new Error('relativePath must not be absolute')
    const fullPath = path.resolve(root.path, relativePath)
    ensureInside(root.path, fullPath)
    return { root, path: fullPath }
  }

  if (!target.path) throw new Error('path or rootId is required')
  const fullPath = path.resolve(expandHome(target.path))
  const root = getRoots().find(item => isInside(item.path, fullPath))
  if (!root) throw new Error(`Path is outside allowed roots: ${fullPath}`)
  if (forWrite && !root.writable) throw new Error(`File root is read-only: ${root.label}`)
  return { root, path: fullPath }
}

async function toEntry(root: DesktopFileRoot, filePath: string): Promise<DesktopFileEntry> {
  const stats = await fs.stat(filePath)
  return {
    name: path.basename(filePath),
    path: filePath,
    relativePath: path.relative(root.path, filePath) || '.',
    rootId: root.id,
    kind: kindForStats(stats),
    size: stats.size,
    createdAt: stats.birthtime.toISOString(),
    modifiedAt: stats.mtime.toISOString(),
  }
}

function builtinRoots(): DesktopFileRoot[] {
  const roots: DesktopFileRoot[] = [
    { id: 'repo', label: '当前项目', path: repoRoot(), writable: true, source: 'builtin' },
    { id: 'desktop', label: '桌面', path: app.getPath('desktop'), writable: true, source: 'builtin' },
    { id: 'documents', label: '文档', path: app.getPath('documents'), writable: true, source: 'builtin' },
  ]
  return roots.map(root => ({ ...root, path: path.resolve(root.path) }))
}

async function grantRoot(rootPath: string): Promise<DesktopFileRoot> {
  const fullPath = path.resolve(rootPath)
  const stats = await fs.stat(fullPath)
  if (!stats.isDirectory()) throw new Error(`Not a directory: ${fullPath}`)

  const roots = loadSelectedRootPaths()
  if (!roots.includes(fullPath)) {
    roots.push(fullPath)
    await fs.writeFile(grantsFilePath(), JSON.stringify({ roots }, null, 2), 'utf8')
  }
  return selectedRoot(fullPath)
}

function loadSelectedRoots(): DesktopFileRoot[] {
  return loadSelectedRootPaths().map(selectedRoot)
}

function loadSelectedRootPaths(): string[] {
  try {
    const raw = readFileSync(grantsFilePath(), 'utf8')
    const parsed = JSON.parse(raw) as { roots?: unknown }
    if (!Array.isArray(parsed.roots)) return []
    return parsed.roots.filter((item): item is string => typeof item === 'string').map(item => path.resolve(item))
  } catch {
    return []
  }
}

function selectedRoot(rootPath: string): DesktopFileRoot {
  const fullPath = path.resolve(rootPath)
  return {
    id: `selected:${createHash('sha1').update(fullPath).digest('hex').slice(0, 12)}`,
    label: path.basename(fullPath) || fullPath,
    path: fullPath,
    writable: true,
    source: 'selected',
  }
}

function grantsFilePath(): string {
  return path.join(app.getPath('userData'), grantsFileName)
}

function expandHome(inputPath: string): string {
  if (inputPath === '~') return app.getPath('home')
  if (inputPath.startsWith(`~${path.sep}`)) return path.join(app.getPath('home'), inputPath.slice(2))
  return inputPath
}

function ensureInside(rootPath: string, filePath: string) {
  if (!isInside(rootPath, filePath)) throw new Error(`Path escapes root: ${filePath}`)
}

function isInside(rootPath: string, filePath: string): boolean {
  const relative = path.relative(path.resolve(rootPath), path.resolve(filePath))
  return relative === '' || (!!relative && !relative.startsWith('..') && !path.isAbsolute(relative))
}

function kindForStats(stats: Awaited<ReturnType<typeof fs.stat>>): DesktopFileKind {
  if (stats.isFile()) return 'file'
  if (stats.isDirectory()) return 'directory'
  return 'other'
}

function compareDirents(left: { name: string; isDirectory: () => boolean }, right: { name: string; isDirectory: () => boolean }): number {
  if (left.isDirectory() !== right.isDirectory()) return left.isDirectory() ? -1 : 1
  return left.name.localeCompare(right.name, 'zh-CN')
}