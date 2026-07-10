export {}

type WorkPartnerFileKind = 'file' | 'directory' | 'other'
type WorkPartnerFileRootSource = 'builtin' | 'selected'

interface WorkPartnerFileRoot {
  id: string
  label: string
  path: string
  writable: boolean
  source: WorkPartnerFileRootSource
}

interface WorkPartnerFileTarget {
  path?: string
  rootId?: string
  relativePath?: string
}

interface WorkPartnerFileEntry {
  name: string
  path: string
  relativePath: string
  rootId: string
  kind: WorkPartnerFileKind
  size: number
  createdAt: string
  modifiedAt: string
}

interface WorkPartnerDirectoryListing {
  root: WorkPartnerFileRoot
  directory: WorkPartnerFileEntry
  entries: WorkPartnerFileEntry[]
}

interface WorkPartnerReadTextFileResult {
  root: WorkPartnerFileRoot
  file: WorkPartnerFileEntry
  content: string
}

interface WorkPartnerWriteTextFileRequest extends WorkPartnerFileTarget {
  content: string
  overwrite?: boolean
  createDirectory?: boolean
}

interface WorkPartnerListDirectoryRequest extends WorkPartnerFileTarget {
  maxEntries?: number
}

interface WorkPartnerReadTextFileRequest extends WorkPartnerFileTarget {
  maxBytes?: number
}

interface WorkPartnerFileSystemApi {
  getRoots: () => Promise<WorkPartnerFileRoot[]>
  selectDirectory: () => Promise<WorkPartnerFileRoot | null>
  selectFiles: () => Promise<WorkPartnerFileEntry[]>
  stat: (target: WorkPartnerFileTarget) => Promise<WorkPartnerFileEntry>
  listDirectory: (request: WorkPartnerListDirectoryRequest) => Promise<WorkPartnerDirectoryListing>
  readTextFile: (request: WorkPartnerReadTextFileRequest) => Promise<WorkPartnerReadTextFileResult>
  writeTextFile: (request: WorkPartnerWriteTextFileRequest) => Promise<WorkPartnerFileEntry>
  createDirectory: (target: WorkPartnerFileTarget) => Promise<WorkPartnerFileEntry>
}

declare global {
  interface Window {
    workpartner?: {
      apiBaseUrl?: string
      platform?: string
      version?: string
      getRuntime?: () => Promise<{
        apiBaseUrl: string
        platform: string
        version: string
      }>
      fileSystem?: WorkPartnerFileSystemApi
    }
  }
}