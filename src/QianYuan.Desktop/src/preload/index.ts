import { contextBridge, ipcRenderer } from 'electron'

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
})