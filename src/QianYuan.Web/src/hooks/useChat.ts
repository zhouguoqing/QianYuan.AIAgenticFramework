import { useCallback, useEffect, useRef, useState } from 'react'
import type { ChatMessageDto, ChunkDto, ComposerMode, ContentPartDto, ImageGenerationOptions, ImagePart, StreamRequest, WorkspaceContext } from '../types/api'
import { generateImage, getSession, prepareRegenerateSession, streamChat } from '../services/api'
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
  const loadSeqRef = useRef(0)

  // Load persisted history when caller switches sessions externally.
  useEffect(() => {
    const seq = ++loadSeqRef.current
    if (!opts.sessionId) {
      setMessages([])
      return
    }
    let alive = true
    getSession(opts.sessionId)
      .then(session => {
        if (!alive || seq !== loadSeqRef.current) return
        setMessages(session.messages.flatMap((message, index) => toDisplayMessages(message, index, session.createdAt)))
      })
      .catch(() => { if (alive && seq === loadSeqRef.current) setMessages([]) })
    return () => { alive = false }
  }, [opts.sessionId])

  const abort = useCallback(() => { abortRef.current?.abort(); abortRef.current = null; setBusy(false) }, [])
  const reset = useCallback(() => {
    abortRef.current?.abort()
    abortRef.current = null
    loadSeqRef.current++
    setBusy(false)
    setMessages([])
  }, [])

  const send = useCallback(async (text: string, images: ImagePart[], mode: ComposerMode = 'chat', workspace?: WorkspaceContext, imageOptions?: ImageGenerationOptions) => {
    loadSeqRef.current++
    const userMsg: DisplayMessage = {
      id: cryptoId(), kind: 'user', text: mode === 'chat' ? text : `${mode === 'text-to-image' ? '文生图' : '图生图'}：${text}`,
      imageUrls: images.map(i => i.url ?? '').filter(Boolean),
      createdAt: new Date().toISOString(),
      model: opts.model ?? 'Auto',
      agentId: opts.agentId ?? undefined,
    }
    setMessages(prev => [...prev, userMsg])

    const autoImageMode = mode === 'chat' && isImageGenerationIntent(text, images)
      ? (images.length > 0 ? 'image-to-image' : 'text-to-image')
      : null

    if (mode === 'text-to-image' || mode === 'image-to-image' || autoImageMode) {
      await sendImageGeneration(text, images, (autoImageMode ?? mode) as 'text-to-image' | 'image-to-image', imageOptions)
      return
    }

    const chatProvider = isImageProviderOrModel(opts.provider, opts.model) ? undefined : opts.provider ?? undefined
    const chatModel = isImageProviderOrModel(opts.provider, opts.model) ? undefined : opts.model ?? undefined

    const req: StreamRequest = {
      agentId: opts.agentId ?? undefined,
      sessionId: opts.sessionId ?? undefined,
      provider: chatProvider,
      model: chatModel,
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

    async function sendImageGeneration(prompt: string, inputImages: ImagePart[], imageMode: 'text-to-image' | 'image-to-image', generationOptions?: ImageGenerationOptions) {
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
          provider: 'openai-image',
          model: 'gpt-image-2',
          optimizePrompt: generationOptions?.optimizePrompt ?? true,
        }, ctrl.signal)
        const imageUrl = result.url ?? (result.base64 ? `data:${result.mime || 'image/png'};base64,${result.base64}` : undefined)
        const promptNote = result.optimizedPrompt ? `\n\n优化提示词：${result.optimizedPrompt}` : ''
        setMessages(prev => prev.map(m => m.id === pendingId ? {
          ...m,
          text: result.revisedPrompt ? `生成完成\n\n${result.revisedPrompt}${promptNote}` : `生成完成${promptNote}`,
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
  }, [opts.agentId, opts.provider, opts.model, opts.skills, opts.sessionId, opts.systemPrompt, opts.onSession])

  const regenerate = useCallback(async (userMessageIndex: number, text: string) => {
    if (!opts.sessionId) return
    const nextText = text.trim()
    if (!nextText) return
    abortRef.current?.abort()

    try {
      setBusy(true)
      const session = await prepareRegenerateSession(opts.sessionId, { userMessageIndex, userText: nextText })
      setMessages(session.messages.flatMap((message, index) => toDisplayMessages(message, index, session.createdAt)))
    } catch (err: any) {
      setMessages(prev => [...prev, { id: cryptoId(), kind: 'error', text: `准备重新生成失败：${String(err?.message ?? err)}`, createdAt: new Date().toISOString() }])
      setBusy(false)
      return
    }

    await send(nextText, [], 'chat')
  }, [opts.sessionId, send])

  return { messages, busy, send, abort, reset, regenerate }
}

function toDisplayMessages(message: ChatMessageDto, index: number, fallbackCreatedAt?: string): DisplayMessage[] {
  const createdAt = fallbackCreatedAt || new Date().toISOString()
  const meta = message.meta ?? {}
  const displayKind = meta.displayKind
  const role = normalizeChatRole(message.role)
  const parts = message.parts.map(part => ({ ...part, kind: normalizeContentKind(part.kind) }))
  const textParts = parts.filter(part => part.kind === 'Text')
  const imageUrls = parts
    .filter(part => part.kind === 'Image' && part.dataUrlOrBase64)
    .map(part => part.dataUrlOrBase64!)
  const toolCalls = parts.filter(part => part.kind === 'ToolCall')
  const toolResults = parts.filter(part => part.kind === 'ToolResult')
  const common = {
    sourceIndex: index,
    createdAt,
    agentId: meta.agentId,
    skillId: meta.skillId,
    step: meta.step ? Number(meta.step) : undefined,
  }

  if (role === 'User') {
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

  if (toolResults.length > 0 || role === 'Tool') {
    const results = toolResults.length > 0 ? toolResults : parts
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

function normalizeChatRole(role: ChatMessageDto['role'] | number): ChatMessageDto['role'] {
  if (typeof role === 'number') {
    return (['System', 'User', 'Assistant', 'Tool'] as const)[role] ?? 'Assistant'
  }
  return role
}

function normalizeContentKind(kind: ContentPartDto['kind'] | number): ContentPartDto['kind'] {
  if (typeof kind === 'number') {
    return (['Text', 'Image', 'Audio', 'File', 'ToolCall', 'ToolResult'] as const)[kind] ?? 'Text'
  }
  return kind
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


function isImageProviderOrModel(provider?: string | null, model?: string | null): boolean {
  return provider === 'openai-image' || !!model?.toLowerCase().startsWith('gpt-image')
}

function isImageGenerationIntent(text: string, images: ImagePart[]): boolean {
  const value = text.trim().toLowerCase()
  if (!value) return false

  const explicitImageActions = [
    '\u751f\u6210\u56fe', '\u751f\u6210\u4e00\u5f20', '\u751f\u6210\u4e00\u4e2a', '\u751f\u6210\u4e00\u5e45', '\u751f\u56fe', '\u753b\u56fe', '\u753b\u4e00', '\u753b\u4e2a', '\u753b\u5f20', '\u753b\u53ea', '\u7ed8\u56fe', '\u7ed8\u5236',
    '\u51fa\u4e00\u5f20', '\u505a\u4e00\u5f20', '\u8bbe\u8ba1\u4e00\u5f20', '\u6587\u751f\u56fe', '\u56fe\u751f\u56fe',
    'generate image', 'create image', 'draw image', 'make image', 'text-to-image', 'image generation'
  ]
  const visualTargets = [
    '\u56fe\u7247', '\u56fe\u50cf', '\u56fe', '\u6d77\u62a5', '\u63d2\u753b', '\u5934\u50cf', '\u56fe\u6807', '\u914d\u56fe', '\u5c01\u9762', '\u58c1\u7eb8',
    'picture', 'poster', 'illustration', 'logo', 'icon', 'avatar', 'wallpaper', 'cover'
  ]
  const generationVerbs = [
    '\u751f\u6210', '\u753b', '\u7ed8\u5236', '\u521b\u4f5c', '\u505a', '\u8bbe\u8ba1', '\u51fa', '\u6765\u4e00\u5f20',
    'generate', 'create', 'draw', 'make', 'design'
  ]
  const editWords = [
    '\u6539\u56fe', '\u4fee\u56fe', '\u6362\u56fe', '\u91cd\u7ed8', '\u53c2\u8003\u56fe', '\u56fe\u7247\u8f6c\u56fe',
    'image-to-image', 'edit image', 'redraw', 'inpaint'
  ]

  if (images.length > 0 && editWords.some(word => value.includes(word))) return true
  if (explicitImageActions.some(word => value.includes(word))) return true
  return generationVerbs.some(verb => value.includes(verb)) && visualTargets.some(target => value.includes(target))
}
