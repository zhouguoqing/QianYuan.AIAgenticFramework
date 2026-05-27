export type ChunkKind =
  | 'Start' | 'TextDelta' | 'ThinkingDelta'
  | 'ToolCallStart' | 'ToolCallArgsDelta' | 'ToolCallEnd' | 'ToolObservation'
  | 'Usage' | 'End' | 'Warning' | 'Error'

export interface ChunkDto {
  kind: ChunkKind
  text?: string | null
  toolCallId?: string | null
  toolName?: string | null
  toolArgsJson?: string | null
  finishReason?: string | null
  model?: string | null
  agentId?: string | null
  skillId?: string | null
  step?: number | null
  usage?: { input: number; output: number; cacheRead?: number; cacheWrite?: number } | null
}

export interface ImagePart { url?: string; base64?: string; mime?: string }

export interface StreamRequest {
  agentId?: string
  sessionId?: string
  ownerId?: string
  userText: string
  images?: ImagePart[]
  provider?: string
  model?: string
  skills?: string[]
  maxIterations?: number
}

export interface AgentDto { id: string; name: string; description: string; tags: string[] }
export interface SkillManifestDto {
  id: string; name: string; description: string; tags: string[]
  approximateToolCount: number; requiresNetwork: boolean; requiresFilesystem: boolean
  enabled: boolean
}
export interface SkillToolDto { name: string; description?: string; jsonSchema?: string; skillId?: string }
export interface SkillToolsResponse {
  skillId: string
  systemPromptFragment?: string | null
  enabled: boolean
  tools: SkillToolDto[]
}
export interface McpStdioRegistrationRequest {
  serverId: string
  command: string
  arguments?: string[]
  environment?: Record<string, string>
}
export interface ProviderDto {
  providerId: string; defaultModel: string; models: string[]; capabilities: string[]
}
export interface ProvidersResponse { defaultProviderId: string | null; providers: ProviderDto[] }
export interface SessionSummaryDto {
  sessionId: string; title?: string | null; agentId?: string | null
  messageCount: number; createdAt: string; updatedAt: string
}
