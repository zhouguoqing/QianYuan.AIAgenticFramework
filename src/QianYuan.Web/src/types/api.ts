export type ChunkKind =
  | 'Session' | 'Runtime' | 'Done'
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
  sessionId?: string | null
  provider?: string | null
  modelSource?: string | null
  usage?: { input: number; output: number; cacheRead?: number; cacheWrite?: number } | null
}

export interface ImagePart { url?: string; base64?: string; mime?: string; name?: string; size?: number }

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
  systemPrompt?: string
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

export interface AuthUserDto {
  id: string
  email: string
  displayName: string
  status: string
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  expiresAt: string
  user: AuthUserDto
}

export interface LoginRequest { email: string; password: string }
export interface RegisterRequest { email: string; password: string; displayName?: string }

export interface CreditWalletDto {
  userId: string
  balance: number
  monthlyQuota: number
  quotaMonth: string
  planId: string
  planName: string
  updatedAt: string
}

export interface CreditTransactionDto {
  id: string
  type: string
  amount: number
  balanceAfter: number
  sourceType: string
  sourceId?: string | null
  description?: string | null
  createdAt: string
}

export interface SubscriptionPlanDto {
  id: string
  name: string
  monthlyCredits: number
  maxAssistants: number
  maxProjects: number
  maxAutoTasks: number
  allowAllModels: boolean
  priceMonthlyCents: number
}

export interface EstimateCreditsRequest {
  inputTokens: number
  outputTokens: number
  modelTier?: string | null
  taskType?: string | null
}

export interface EstimateCreditsResponse {
  estimatedCredits: number
  multiplier: number
  formula: string
}

export interface CreateWorkTaskRequest {
  title: string
  goal: string
  teamId?: string | null
  providerId?: string | null
  model?: string | null
}

export interface WorkTaskDto {
  id: string
  title: string
  goal: string
  status: string
  teamId?: string | null
  providerId?: string | null
  model?: string | null
  createdAt: string
  updatedAt: string
  stepCount: number
  artifactCount: number
}

export interface WorkStepDto {
  id: string
  stepOrder: number
  name: string
  status: string
  agentId?: string | null
  summary?: string | null
  createdAt: string
  updatedAt: string
}

export interface WorkArtifactDto {
  id: string
  taskId: string
  name: string
  contentType: string
  storageKind: string
  content?: string | null
  filePath?: string | null
  sizeBytes: number
  createdAt: string
}

export interface WorkTaskDetailDto {
  task: WorkTaskDto
  steps: WorkStepDto[]
  artifacts: WorkArtifactDto[]
}

export interface ExpertTeamMemberDto {
  id: string
  memberOrder: number
  roleId: string
  displayName: string
  agentId: string
  responsibility: string
  executionMode: string
  enabled: boolean
}

export interface ExpertTeamDto {
  id: string
  name: string
  description: string
  scenario: string
  enabled: boolean
  createdAt: string
  updatedAt: string
  members: ExpertTeamMemberDto[]
}

// Expert marketplace (WorkBuddy-style catalog)
export interface ExpertCategoryDto {
  id: string
  name: string
  description: string
  count: number
}

export interface ExpertSummaryDto {
  id: string
  categoryId: string
  categoryName: string
  name: string
  profession: string
  description: string
  avatarUrl: string
  type: string
  isOpc: boolean
  tags: string[]
  author?: string | null
}

export interface ExpertDetailDto extends ExpertSummaryDto {
  agentName: string
  plugin: string
  defaultInitPrompt: string
  quickPrompts: string[]
}

export interface ExpertScenarioDto {
  id: string
  name: string
  description: string
  accent: string
  experts: ExpertSummaryDto[]
}

export interface ExpertListResultDto {
  total: number
  items: ExpertSummaryDto[]
}

export interface ExpertPromptDto {
  id: string
  systemPrompt: string
}

// Knowledge base types
export interface KnowledgeDocument {
  id: string; title: string; content: string; tags: string[]; createdAt: string;
  sourceFile?: string | null; sourceSection?: string | null;
}
export interface KnowledgeSearchResult { matches: KnowledgeDocument[]; answer?: string | null }
