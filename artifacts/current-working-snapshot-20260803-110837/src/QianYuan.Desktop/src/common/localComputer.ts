export interface LocalCommandStartRequest {
  command: string
  args?: string[]
  cwd?: string
  timeoutMs?: number
  env?: Record<string, string>
}

export interface LocalCommandRuntime {
  id: string
  pid?: number
  command: string
  args: string[]
  cwd: string
  status: 'running' | 'completed' | 'failed' | 'canceled' | 'timeout'
  startedAt: string
  finishedAt?: string
  exitCode?: number | null
  signal?: string | null
  stdout: string
  stderr: string
  error?: string
}

export const localComputerChannels = {
  startCommand: 'workpartner:computer:startCommand',
  getCommand: 'workpartner:computer:getCommand',
  listCommands: 'workpartner:computer:listCommands',
  cancelCommand: 'workpartner:computer:cancelCommand',
} as const
