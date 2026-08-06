import { useCallback, useEffect, useRef, useState } from 'react'
import type { ChatMessageDto, ChunkDto, ComposerMode, ContentPartDto, ImagePart, StreamRequest, WorkspaceContext } from '../types/api'
import { generateImage, getSession, streamChat } from '../services/api'
import type { DisplayMessage } from '../components/ChatMessageView'

interface UseChatOptions {
  agentId: string | null
  provider: string | null
  model: string | null
  skills: string[]
  sessionId: string | null
  systemPrompt?: string | null
  onSession: (id: string) => void
}

/**
 * Drives a single conversation: sends user turns, accumulates streamed assistant text,
 * surfaces tool calls / observations / thinking as separate display messages.
 */
export function useChat(opts: UseChatOptions) {
  const [messages, setMessages] = useState<DisplayMessage[]>([])
  const [busy, setBusy] = useState(false)
  const abortRef = useRef<AbortController | null>(null)

  // Load persisted history when caller switches sessions externally.
  useEffect(() => {
    if (!opts.sessionId) {
      setMessages([])
      return
    }
    let alive = true
    getSession(opts.sessionId)
      .then(session => {
        if (!alive) return
        setMessages(session.messages.flatMap((message, index) => toDisplayMessages(message, index, session.createdAt)))
      })
      .catch(() => { if (alive) setMessages([]) })
    return () => { alive = false }
  }, [opts.sessionId])

  const abort = useCallback(() => { abortRef.current?.abort(); abortRef.current = null; setBusy(false) }, [])
  const reset = useCallback(() => {
    abortRef.current?.abort()
    abortRef.current = null
    setBusy(false)
    setMessages([])
  }, [])

  const send = useCallback(async (text: string, images: ImagePart[], mode: ComposerMode = 'chat', workspace?: WorkspaceContext) => {
    const userMsg: DisplayMessage = {
      id: cryptoId(), kind: 'user', text: mode === 'chat' ? text : `${mode === 'text-to-image' ? '文生图' : '图生图'}：${text}`,
      imageUrls: images.map(i => i.url ?? '').filter(Boolean),
      createdAt: new Date().toISOString(),
      model: opts.model ?? 'Auto',
      agentId: opts.agentId ?? undefined,
    }
    setMessages(prev => [...prev, userMsg])

    if (mode === 'text-to-image' || mode === 'image-to-image') {
      await sendImageGeneration(text, images, mode)
      return
    }

    const req: StreamRequest = {
      agentId: opts.agentId ?? undefined,
      sessionId: opts.sessionId ?? undefined,
      provider: opts.provider ?? undefined,
      model: opts.model ?? undefined,
      skills: opts.skills.length > 0 ? opts.skills : undefined,
      userText: text,
      images: images.length > 0 ? images : undefined,
      systemPrompt: opts.systemPrompt ?? undefined,
      workspaceId: workspace?.workspaceId,
      workspacePath: workspace?.workspacePath,
      workspaceLabel: workspace?.workspaceLabel,
      permission: workspace?.permission,
    }

    const ctrl = new AbortController()
    abortRef.current = ctrl
    setBusy(true)

    // Track current assistant streaming message id; tool & observation use their own.
    let assistantId: string | null = null
    let toolStreamIds: Record<string, string> = {}
    let runtime: { provider?: string | null; model?: string | null; modelSource?: string | null } = {}

    try {
      for await (const chunk of streamChat(req, ctrl.signal)) {
        applyChunk(chunk)
      }
    } catch (err: any) {
      if (err?.name !== 'AbortError')
        setMessages(prev => [...prev, { id: cryptoId(), kind: 'error', text: String(err?.message ?? err) }])
    } finally {
      setBusy(false)
      abortRef.current = null
      setMessages(prev => prev.map(m => m.streaming ? { ...m, streaming: false } : m))
    }

    async function sendImageGeneration(prompt: string, inputImages: ImagePart[], imageMode: 'text-to-image' | 'image-to-image') {
      const ctrl = new AbortController()
      abortRef.current = ctrl
      setBusy(true)
      const pendingId = cryptoId()
      setMessages(prev => [...prev, { id: pendingId, kind: 'assistant', text: '正在生成图片...', streaming: true }])

      try {
        const result = await generateImage({
          mode: imageMode,
          prompt,
          images: imageMode === 'image-to-image' ? inputImages : undefined,
          provider: opts.provider ?? undefined,
        }, ctrl.signal)
        const imageUrl = result.url ?? (result.base64 ? `data:${result.mime || 'image/png'};base64,${result.base64}` : undefined)
        setMessages(prev => prev.map(m => m.id === pendingId ? {
          ...m,
          text: result.revisedPrompt ? `生成完成\n\n${result.revisedPrompt}` : '生成完成',
          imageUrls: imageUrl ? [imageUrl] : undefined,
          streaming: false,
        } : m))
      } catch (err: any) {
        if (err?.name !== 'AbortError') {
          setMessages(prev => prev.map(m => m.id === pendingId ? {
            ...m,
            kind: 'error',
            text: String(err?.message ?? err),
            streaming: false,
          } : m))
        }
      } finally {
        setBusy(false)
        abortRef.current = null
      }
    }

    function applyChunk(c: ChunkDto) {
      switch (c.kind) {
        case 'Session':
          if (c.sessionId) opts.onSession(c.sessionId)
          break

        case 'Runtime':
          runtime = { provider: c.provider, model: c.model, modelSource: c.modelSource }
          break

        case 'Start':
          assistantId = cryptoId()
          setMessages(prev => [...prev, {
            id: assistantId!, kind: 'assistant', text: '', streaming: true,
            createdAt: new Date().toISOString(),
            model: c.model ?? runtime.model ?? opts.model ?? undefined,
            provider: runtime.provider ?? opts.provider ?? undefined,
            modelSource: runtime.modelSource ?? 'cloud',
            agentId: c.agentId ?? opts.agentId ?? undefined,
            step: c.step ?? undefined,
          }])
          break

        case 'TextDelta':
          if (!c.text) return
          if (!assistantId) {
            assistantId = cryptoId()
            setMessages(prev => [...prev, {
              id: assistantId!, kind: 'assistant', text: c.text!, streaming: true,
              createdAt: new Date().toISOString(),
              model: c.model ?? runtime.model ?? opts.model ?? undefined,
              provider: runtime.provider ?? opts.provider ?? undefined,
              modelSource: runtime.modelSource ?? 'cloud',
              agentId: c.agentId ?? opts.agentId ?? undefined,
              step: c.step ?? undefined,
            }])
          } else {
            const id = assistantId
            setMessages(prev => prev.map(m => m.id === id ? {
              ...m,
              text: m.text + c.text,
              model: c.model ?? m.model,
              agentId: c.agentId ?? m.agentId,
              step: c.step ?? m.step,
            } : m))
          }
          break

        case 'ThinkingDelta':
          if (!c.text) return
          setMessages(prev => {
            const last = prev[prev.length - 1]
            if (last && last.kind === 'thinking' && last.streaming) {
              return prev.map((m, i) => i === prev.length - 1 ? { ...m, text: m.text + c.text } : m)
            }
            return [...prev, {
              id: cryptoId(), kind: 'thinking', text: c.text!, streaming: true,
              createdAt: new Date().toISOString(),
              model: c.model ?? runtime.model ?? undefined,
              provider: runtime.provider ?? undefined,
              modelSource: runtime.modelSource ?? 'cloud',
              agentId: c.agentId ?? opts.agentId ?? undefined,
              step: c.step ?? undefined,
            }]
          })
          break

        case 'ToolCallStart': {
          // Tool call ends the current assistant message; start a fresh one after the tool result.
          if (assistantId) setMessages(prev => prev.map(m => m.id === assistantId ? { ...m, streaming: false } : m))
          assistantId = null
          const id = cryptoId()
          if (c.toolCallId) toolStreamIds[c.toolCallId] = id
          setMessages(prev => [...prev, {
            id, kind: 'tool',
            toolName: c.toolName ?? undefined,
            text: c.toolArgsJson ?? '',
            streaming: true,
            createdAt: new Date().toISOString(),
            skillId: c.skillId ?? undefined,
            step: c.step ?? undefined,
          }])
          break
        }
        case 'ToolCallArgsDelta': {
          if (!c.toolCallId || !c.toolArgsJson) return
          const id = toolStreamIds[c.toolCallId]
          if (!id) return
          setMessages(prev => prev.map(m => m.id === id ? { ...m, text: m.text + c.toolArgsJson } : m))
          break
        }
        case 'ToolCallEnd': {
          if (!c.toolCallId) return
          const id = toolStreamIds[c.toolCallId]
          if (!id) return
          setMessages(prev => prev.map(m => m.id === id ? { ...m, streaming: false } : m))
          break
        }
        case 'ToolObservation': {
          setMessages(prev => [...prev, {
            id: cryptoId(),
            kind: 'observation',
            toolName: c.toolName ?? undefined,
            text: c.text ?? '',
            createdAt: new Date().toISOString(),
            skillId: c.skillId ?? undefined,
            step: c.step ?? undefined,
          }])
          break
        }
        case 'Warning':
          setMessages(prev => [...prev, { id: cryptoId(), kind: 'warning', text: c.text ?? '', createdAt: new Date().toISOString() }]); break
        case 'Error':
          setMessages(prev => [...prev, { id: cryptoId(), kind: 'error', text: c.text ?? '', createdAt: new Date().toISOString() }]); break
        case 'End':
          if (assistantId) {
            const id = assistantId
            setMessages(prev => prev.map(m => m.id === id ? { ...m, streaming: false, usage: c.usage ?? m.usage } : m))
          }
          break
        case 'Usage':
          if (assistantId && c.usage) {
            const id = assistantId
            setMessages(prev => prev.map(m => m.id === id ? { ...m, usage: c.usage ?? m.usage } : m))
          }
          break
        case 'Done':
          if (c.sessionId) opts.onSession(c.sessionId)
          break
      }
    }
  }, [opts.agentId, opts.provider, opts.model, opts.skills, opts.sessionId, opts.onSession])

  return { messages, busy, send, abort, reset }
}

function toDisplayMessages(message: ChatMessageDto, index: number, fallbackCreatedAt?: string): DisplayMessage[] {
  const createdAt = fallbackCreatedAt || new Date().toISOString()
  const meta = message.meta ?? {}
  const displayKind = meta.displayKind
  const textParts = message.parts.filter(part => part.kind === 'Text')
  const imageUrls = message.parts
    .filter(part => part.kind === 'Image' && part.dataUrlOrBase64)
    .map(part => part.dataUrlOrBase64!)
  const toolCalls = message.parts.filter(part => part.kind === 'ToolCall')
  const toolResults = message.parts.filter(part => part.kind === 'ToolResult')
  const common = {
    createdAt,
    agentId: meta.agentId,
    skillId: meta.skillId,
    step: meta.step ? Number(meta.step) : undefined,
  }

  if (message.role === 'User') {
    return [{
      id: stableMessageId(index, 'user'),
      kind: 'user',
      text: textParts.map(part => part.text ?? '').join(''),
      imageUrls: imageUrls.length > 0 ? imageUrls : undefined,
      ...common,
    }]
  }

  if (toolCalls.length > 0) {
    return toolCalls.map((part, i) => ({
      id: stableMessageId(index, `tool-${i}`),
      kind: 'tool' as const,
      toolName: part.name ?? meta.toolName,
      text: part.jsonPayload ?? '',
      ...common,
    }))
  }

  if (toolResults.length > 0 || message.role === 'Tool') {
    const results = toolResults.length > 0 ? toolResults : message.parts
    return results.map((part, i) => ({
      id: stableMessageId(index, `observation-${i}`),
      kind: 'observation' as const,
      toolName: part.name ?? meta.toolName,
      text: toolResultText(part),
      ...common,
    }))
  }

  const kind = displayKind === 'thinking' ? 'thinking'
    : displayKind === 'warning' ? 'warning'
    : displayKind === 'error' ? 'error'
    : 'assistant'

  return [{
    id: stableMessageId(index, kind),
    kind,
    text: textParts.map(part => part.text ?? '').join(''),
    imageUrls: imageUrls.length > 0 ? imageUrls : undefined,
    ...common,
  }]
}

function toolResultText(part: ContentPartDto) {
  return part.text || part.jsonPayload || ''
}

function stableMessageId(index: number, suffix: string) {
  return `persisted-${index}-${suffix}`
}

function cryptoId(): string {
  return (globalThis.crypto as Crypto).randomUUID()
}
