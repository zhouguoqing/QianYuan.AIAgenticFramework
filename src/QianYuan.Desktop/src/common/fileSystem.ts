export type DesktopFileKind = 'file' | 'directory' | 'other'
export type DesktopFileRootSource = 'builtin' | 'selected'

export interface DesktopFileRoot {
  id: string
  label: string
  path: string
  writable: boolean
  source: DesktopFileRootSource
}

export interface DesktopFileTarget {
  path?: string
  rootId?: string
  relativePath?: string
}

export interface DesktopFileEntry {
  name: string
  path: string
  relativePath: string
  rootId: string
  kind: DesktopFileKind
  size: number
  createdAt: string
  modifiedAt: string
}

export interface DesktopDirectoryListing {
  root: DesktopFileRoot
  directory: DesktopFileEntry
  entries: DesktopFileEntry[]
}

export interface DesktopReadTextFileResult {
  root: DesktopFileRoot
  file: DesktopFileEntry
  content: string
}

export interface DesktopWriteTextFileRequest extends DesktopFileTarget {
  content: string
  overwrite?: boolean
  createDirectory?: boolean
}

export interface DesktopListDirectoryRequest extends DesktopFileTarget {
  maxEntries?: number
}

export interface DesktopReadTextFileRequest extends DesktopFileTarget {
  maxBytes?: number
}

export const desktopFileSystemChannels = {
  getRoots: 'workpartner:fs:getRoots',
  selectDirectory: 'workpartner:fs:selectDirectory',
  selectFiles: 'workpartner:fs:selectFiles',
  stat: 'workpartner:fs:stat',
  listDirectory: 'workpartner:fs:listDirectory',
  readTextFile: 'workpartner:fs:readTextFile',
  writeTextFile: 'workpartner:fs:writeTextFile',
  createDirectory: 'workpartner:fs:createDirectory',
} as const