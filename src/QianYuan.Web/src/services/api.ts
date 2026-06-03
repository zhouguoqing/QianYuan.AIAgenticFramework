import type {
  ChunkDto, StreamRequest, ImageGenerationRequest, ImageGenerationResponse,
  AgentDto, SkillManifestDto, SkillToolsResponse, McpStdioRegistrationRequest,
  ProvidersResponse, SessionSummaryDto, KnowledgeDocument, KnowledgeSearchResult,
  AgentStoreAgentDto, CreateAgentStoreAgentRequest, AddAgentSkillRequest,
  AddAgentMcpServerRequest, AddAgentCliServiceRequest, AgentStoreToolDto,
  AgentInteractChunk,
} from '../types/api'

const API = '/api'

export async function listAgents(): Promise<AgentDto[]> {
  const r = await fetch(`${API}/agents`); return r.json()
}
export async function listSkills(): Promise<SkillManifestDto[]> {
  const r = await fetch(`${API}/skills`); return r.json()
}
export async function listSkillTools(skillId: string): Promise<SkillToolsResponse | null> {
  const r = await fetch(`${API}/skills/${encodeURIComponent(skillId)}/tools`)
  if (!r.ok) return null
  return r.json()
}
export async function setSkillEnabled(skillId: string, enabled: boolean): Promise<boolean> {
  const r = await fetch(`${API}/skills/${encodeURIComponent(skillId)}/enabled`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ enabled }),
  })
  return r.ok
}
export async function relevantSkills(q: string, topK = 8): Promise<SkillManifestDto[]> {
  const r = await fetch(`${API}/skills/relevant?q=${encodeURIComponent(q)}&topK=${topK}`)
  if (!r.ok) return []
  return r.json()
}
export async function registerMcpStdio(req: McpStdioRegistrationRequest): Promise<{ ok: boolean; error?: string; skillId?: string }> {
  const r = await fetch(`${API}/skills/register/mcp-stdio`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  })
  if (r.ok) {
    const body = await r.json()
    return { ok: true, skillId: body.skillId }
  }
  const text = await r.text()
  return { ok: false, error: text }
}
export async function listProviders(): Promise<ProvidersResponse> {
  const r = await fetch(`${API}/providers`); return r.json()
}
export async function listSessions(): Promise<SessionSummaryDto[]> {
  const r = await fetch(`${API}/sessions`); return r.json()
}
export async function deleteSession(id: string) {
  await fetch(`${API}/sessions/${id}`, { method: 'DELETE' })
}

async function readJsonOrThrow<T>(resp: Response): Promise<T> {
  if (!resp.ok) {
    const text = await resp.text()
    throw new Error(text || `HTTP ${resp.status}`)
  }
  return resp.json()
}

export async function listAgentStoreAgents(): Promise<AgentStoreAgentDto[]> {
  return readJsonOrThrow(await fetch(`${API}/agent-store`))
}
export async function getAgentStoreAgent(agentId: string): Promise<AgentStoreAgentDto> {
  return readJsonOrThrow(await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}`))
}
export async function createAgentStoreAgent(req: CreateAgentStoreAgentRequest): Promise<AgentStoreAgentDto> {
  return readJsonOrThrow(await fetch(`${API}/agent-store`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(req),
  }))
}
export async function updateAgentStoreAgent(agentId: string, req: CreateAgentStoreAgentRequest): Promise<AgentStoreAgentDto> {
  return readJsonOrThrow(await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}`, {
    method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(req),
  }))
}
export async function deleteAgentStoreAgent(agentId: string) {
  const r = await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}`, { method: 'DELETE' })
  if (!r.ok) throw new Error(await r.text())
}
export async function addAgentStoreSkill(agentId: string, req: AddAgentSkillRequest) {
  return readJsonOrThrow(await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}/skills`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(req),
  }))
}
export async function removeAgentStoreSkill(agentId: string, skillRowId: number) {
  const r = await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}/skills/${skillRowId}`, { method: 'DELETE' })
  if (!r.ok) throw new Error(await r.text())
}
export async function addAgentStoreMcpServer(agentId: string, req: AddAgentMcpServerRequest) {
  return readJsonOrThrow(await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}/mcp-servers`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(req),
  }))
}
export async function removeAgentStoreMcpServer(agentId: string, serverRowId: number) {
  const r = await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}/mcp-servers/${serverRowId}`, { method: 'DELETE' })
  if (!r.ok) throw new Error(await r.text())
}
export async function addAgentStoreCliService(agentId: string, req: AddAgentCliServiceRequest) {
  return readJsonOrThrow(await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}/cli-services`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(req),
  }))
}
export async function removeAgentStoreCliService(agentId: string, serviceRowId: number) {
  const r = await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}/cli-services/${serviceRowId}`, { method: 'DELETE' })
  if (!r.ok) throw new Error(await r.text())
}
export async function listAgentStoreTools(agentId: string): Promise<AgentStoreToolDto[]> {
  return readJsonOrThrow(await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}/tools`))
}
export async function testAgentStoreTool(agentId: string, toolName: string, args: string): Promise<string> {
  const r = await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}/test-tool`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ toolName, arguments: args || '{}' }),
  })
  if (!r.ok) throw new Error(await r.text())
  return r.text()
}
export async function interactAgentStore(agentId: string, message: string): Promise<AgentInteractChunk[]> {
  return readJsonOrThrow(await fetch(`${API}/agent-store/${encodeURIComponent(agentId)}/interact`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ message }),
  }))
}

export async function generateImage(req: ImageGenerationRequest, signal: AbortSignal): Promise<ImageGenerationResponse> {
  const r = await fetch(`${API}/images/generate`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
    signal,
  })
  if (!r.ok) {
    const text = await r.text()
    throw new Error(text || `image HTTP ${r.status}`)
  }
  return r.json()
}

// Knowledge base API
export async function listKnowledge(): Promise<KnowledgeDocument[]> {
  const r = await fetch(`${API}/knowledge`)
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}
export async function uploadKnowledge(req: { title?: string; content: string; tags?: string[] }) {
  const r = await fetch(`${API}/knowledge/upload`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(req),
  });
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}
export async function uploadKnowledgeFile(body: FormData) {
  const r = await fetch(`${API}/knowledge/upload-file`, {
    method: 'POST', body,
  });
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}
export async function searchKnowledge(q: string, topK = 5, answer = false, provider?: string): Promise<KnowledgeSearchResult> {
  const params = new URLSearchParams({ q, topK: String(topK), answer: String(answer) })
  if (provider) params.set('provider', provider)
  const r = await fetch(`${API}/knowledge/search?${params.toString()}`)
  if (!r.ok) throw new Error(await r.text())
  return r.json()
}
export async function getKnowledge(id: string): Promise<KnowledgeDocument> {
  const r = await fetch(`${API}/knowledge/${encodeURIComponent(id)}`)
  if (!r.ok) throw new Error('not found')
  return r.json()
}
export async function deleteKnowledge(id: string) {
  const r = await fetch(`${API}/knowledge/${encodeURIComponent(id)}`, { method: 'DELETE' })
  if (!r.ok) throw new Error(await r.text())
}

/**
 * POST /api/chat/stream returns text/event-stream. We parse SSE frames as they arrive and yield
 * typed ChunkDto values. The caller controls cancellation via AbortSignal.
 */
export async function* streamChat(req: StreamRequest, signal: AbortSignal): AsyncGenerator<ChunkDto> {
  const resp = await fetch(`${API}/chat/stream`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept': 'text/event-stream' },
    body: JSON.stringify(req),
    signal,
  })
  if (!resp.ok || !resp.body) {
    throw new Error(`stream HTTP ${resp.status}`)
  }
  const reader = resp.body.getReader()
  const decoder = new TextDecoder('utf-8')
  let buffer = ''

  while (true) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })

    // SSE events are separated by a blank line.
    let idx: number
    while ((idx = buffer.indexOf('\n\n')) !== -1) {
      const raw = buffer.slice(0, idx)
      buffer = buffer.slice(idx + 2)
      const event = parseSseEvent(raw)
      if (event) yield event
    }
  }
}

function parseSseEvent(raw: string): ChunkDto | null {
  let dataLines: string[] = []
  let eventName: string | null = null
  for (const line of raw.split('\n')) {
    if (line.startsWith('event:')) eventName = line.slice(6).trim()
    else if (line.startsWith('data:')) dataLines.push(line.slice(5).trimStart())
  }
  if (dataLines.length === 0) return null
  const dataStr = dataLines.join('\n')
  try {
    // Envelope events carry metadata only — the real Start/End chunks come on their own.
    if (eventName === 'session' || eventName === 'done') return null
    const obj = JSON.parse(dataStr) as ChunkDto
    if (eventName === 'error') return { kind: 'Error', text: (obj as any).message ?? 'error' }
    return obj
  } catch {
    return null
  }
}
