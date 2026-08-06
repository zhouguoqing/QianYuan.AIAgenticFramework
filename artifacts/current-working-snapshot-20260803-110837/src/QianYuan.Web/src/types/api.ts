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
  workspaceId?: string
  workspacePath?: string
  workspaceLabel?: string
  permission?: string
}

export interface WorkspaceContext {
  workspaceId?: string
  workspacePath?: string
  workspaceLabel?: string
  permission?: string
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
  category?: string
  triggerPhrases?: string[]
  enabled: boolean
}

export interface SkillCategoryDto { id: string; name: string; marketCount: number; installedCount: number }
export interface SkillMarketEntryDto {
  id: string
  packageId: string
  packageName: string
  name: string
  description: string
  category: string
  tags: string[]
  triggerPhrases: string[]
  source: string
  sourceUrl?: string | null
  installed: boolean
  installedSkillId?: string | null
  enabled: boolean
}
export interface SkillPackageDto {
  id: string
  name: string
  description: string
  category: string
  sortOrder: number
  entries: SkillMarketEntryDto[]
}
export interface InstalledSkillDto {
  skillId: string
  marketEntryId?: string | null
  name: string
  description: string
  category: string
  tags: string[]
  triggerPhrases: string[]
  scope: string
  installPath: string
  enabled: boolean
  installedAt: string
  updatedAt: string
}
export interface InstallSkillRequest { marketEntryId: string; enabled?: boolean }
export interface CreateSkillRequest {
  id: string
  name: string
  description: string
  body: string
  category?: string | null
  tags?: string[] | null
  triggerPhrases?: string[] | null
  scope?: string | null
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
export type ChatRoleDto = 'System' | 'User' | 'Assistant' | 'Tool'
export type ContentKindDto = 'Text' | 'Image' | 'Audio' | 'File' | 'ToolCall' | 'ToolResult'
export interface ContentPartDto {
  kind: ContentKindDto
  text?: string | null
  dataUrlOrBase64?: string | null
  mimeType?: string | null
  name?: string | null
  toolCallId?: string | null
  jsonPayload?: string | null
}
export interface ChatMessageDto {
  role: ChatRoleDto
  parts: ContentPartDto[]
  name?: string | null
  meta?: Record<string, string> | null
}
export interface SessionSummaryDto {
  sessionId: string; title?: string | null; agentId?: string | null
  messageCount: number; createdAt: string; updatedAt: string
}
export interface SessionStateDto extends SessionSummaryDto {
  ownerId?: string | null
  messages: ChatMessageDto[]
  metadata?: Record<string, string> | null
}
export interface SessionCreateRequest { sessionId?: string; ownerId?: string; title?: string; agentId?: string }
export interface SessionUpdateRequest { title?: string | null; agentId?: string | null }

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
  executionMode: string
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

export interface WorkTaskRuntimeDto {
  taskId: string
  status: string
  isRunning: boolean
  startedAt: string
  finishedAt?: string | null
  lastError?: string | null
  cancelReason?: string | null
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


export interface CreateExpertTeamMemberRequest {
  roleId: string
  displayName: string
  agentId?: string | null
  responsibility: string
  executionMode?: string | null
}

export interface UpdateExpertTeamMemberRequest extends CreateExpertTeamMemberRequest {
  memberOrder?: number | null
  enabled?: boolean | null
}

export interface CreateExpertTeamRequest {
  name: string
  description?: string | null
  scenario?: string | null
  members?: CreateExpertTeamMemberRequest[] | null
}

export interface UpdateExpertTeamRequest {
  name: string
  description?: string | null
  scenario?: string | null
  enabled?: boolean | null
}

export interface ExpertTeamTemplateMemberDto {
  roleId: string
  displayName: string
  profession: string
  responsibility: string
  executionMode: string
}

export interface ExpertTeamTemplateDto {
  id: string
  name: string
  description: string
  scenario: string
  categoryId: string
  tags: string[]
  defaultInitPrompt: string
  members: ExpertTeamTemplateMemberDto[]
}

export interface ExpertTeamExecutionEventDto {
  type: string
  taskId: string
  teamId?: string | null
  stepId?: string | null
  stepOrder?: number | null
  stepName?: string | null
  executionMode?: string | null
  status: string
  message?: string | null
  at: string
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
  isCustom: boolean
  boundAgentId?: string | null
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
  boundAgentId?: string | null
}

export interface CustomExpertUpsertRequest {
  id?: string | null
  name: string
  profession: string
  description: string
  systemPrompt: string
  categoryId?: string | null
  avatarUrl?: string | null
  tags?: string[] | null
  quickPrompts?: string[] | null
  boundAgentId?: string | null
  author?: string | null
}

export interface ExpertChatRequest {
  message: string
  quickPrompt?: string | null
  provider?: string | null
  model?: string | null
}

export interface ExpertChatResponse {
  expertId: string
  boundAgentId?: string | null
  content: string
  chunks: string[]
}

// Knowledge base types
export interface KnowledgeDocument {
  id: string; title: string; content: string; tags: string[]; createdAt: string;
  sourceFile?: string | null; sourceSection?: string | null;
}
export interface KnowledgeSearchResult { matches: KnowledgeDocument[]; answer?: string | null }
