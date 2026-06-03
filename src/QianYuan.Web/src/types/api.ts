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

export type ComposerMode = 'chat' | 'text-to-image' | 'image-to-image'

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

export interface ImageGenerationRequest {
  mode: 'text-to-image' | 'image-to-image'
  prompt: string
  images?: ImagePart[]
  provider?: string
  model?: string
  size?: string
}

export interface ImageGenerationResponse {
  provider: string
  model: string
  url?: string | null
  base64?: string | null
  mime: string
  revisedPrompt?: string | null
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

export interface AgentStoreSkillDto {
  id: number
  skillId: string
  enabled: boolean
  priority: number
}

export interface AgentStoreMcpServerDto {
  id: number
  mcpServerId: string
  serverName: string
  enabled: boolean
}

export interface AgentStoreCliServiceDto {
  id: number
  cliServiceId: string
  serviceName: string
  baseUri: string
  enabled: boolean
}

export interface AgentStoreAgentDto {
  id: string
  name: string
  description?: string | null
  defaultProviderId: string
  defaultModel: string
  systemPrompt?: string | null
  skills: AgentStoreSkillDto[]
  mcpServers: AgentStoreMcpServerDto[]
  cliServices: AgentStoreCliServiceDto[]
  createdAt: string
  updatedAt: string
  enabled: boolean
}

export interface CreateAgentStoreAgentRequest {
  id: string
  name: string
  description?: string | null
  defaultProviderId?: string | null
  defaultModel?: string | null
  systemPrompt?: string | null
}

export interface AddAgentSkillRequest { skillId: string; priority: number }
export interface AddAgentMcpServerRequest {
  mcpServerId: string
  serverName: string
  command: string
  arguments?: string[]
}
export interface AddAgentCliServiceRequest {
  cliServiceId: string
  serviceName: string
  baseUri: string
  authConfig?: unknown
}
export interface AgentStoreToolDto {
  name: string
  description?: string
  jsonSchema?: string
  skillId?: string
}
export interface AgentInteractChunk { type: 'text' | 'error'; content: string }

// Knowledge base types
export interface KnowledgeDocument {
  id: string; title: string; content: string; tags: string[]; createdAt: string;
  sourceFile?: string | null; sourceSection?: string | null;
}
export interface KnowledgeSearchResult { matches: KnowledgeDocument[]; answer?: string | null }
