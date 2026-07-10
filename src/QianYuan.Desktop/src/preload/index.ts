import { contextBridge, ipcRenderer } from 'electron'
import {
  desktopFileSystemChannels,
  DesktopDirectoryListing,
  DesktopFileEntry,
  DesktopFileRoot,
  DesktopFileTarget,
  DesktopListDirectoryRequest,
  DesktopReadTextFileRequest,
  DesktopReadTextFileResult,
  DesktopWriteTextFileRequest,
} from '../common/fileSystem.js'

interface WorkPartnerRuntime {
  apiBaseUrl: string
  platform: NodeJS.Platform
  version: string
}

const apiArg = process.argv.find(arg => arg.startsWith('--workpartner-api-url='))
const apiBaseUrl = apiArg?.split('=')[1] ?? 'http://127.0.0.1:5050'

contextBridge.exposeInMainWorld('workpartner', {
  apiBaseUrl,
  platform: process.platform,
  version: process.versions.electron,
  getRuntime: async (): Promise<WorkPartnerRuntime> => ipcRenderer.invoke('workpartner:getRuntime'),
  fileSystem: {
    getRoots: async (): Promise<DesktopFileRoot[]> => ipcRenderer.invoke(desktopFileSystemChannels.getRoots),
    selectDirectory: async (): Promise<DesktopFileRoot | null> => ipcRenderer.invoke(desktopFileSystemChannels.selectDirectory),
    selectFiles: async (): Promise<DesktopFileEntry[]> => ipcRenderer.invoke(desktopFileSystemChannels.selectFiles),
    stat: async (target: DesktopFileTarget): Promise<DesktopFileEntry> => ipcRenderer.invoke(desktopFileSystemChannels.stat, target),
    listDirectory: async (request: DesktopListDirectoryRequest): Promise<DesktopDirectoryListing> => ipcRenderer.invoke(desktopFileSystemChannels.listDirectory, request),
    readTextFile: async (request: DesktopReadTextFileRequest): Promise<DesktopReadTextFileResult> => ipcRenderer.invoke(desktopFileSystemChannels.readTextFile, request),
    writeTextFile: async (request: DesktopWriteTextFileRequest): Promise<DesktopFileEntry> => ipcRenderer.invoke(desktopFileSystemChannels.writeTextFile, request),
    createDirectory: async (target: DesktopFileTarget): Promise<DesktopFileEntry> => ipcRenderer.invoke(desktopFileSystemChannels.createDirectory, target),
  },
})