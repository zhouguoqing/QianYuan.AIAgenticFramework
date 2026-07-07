export {}

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
    }
  }
}