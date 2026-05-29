import type {
  ChunkDto, StreamRequest, ImageGenerationRequest, ImageGenerationResponse,
  AgentDto, SkillManifestDto, SkillToolsResponse, McpStdioRegistrationRequest,
  ProvidersResponse, SessionSummaryDto
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
