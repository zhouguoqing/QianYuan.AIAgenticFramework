import { useCallback, useEffect, useRef, useState } from 'react'
import type { ChunkDto, ComposerMode, ImagePart, StreamRequest } from '../types/api'
import { generateImage, streamChat } from '../services/api'
import type { DisplayMessage } from '../components/ChatMessageView'

interface UseChatOptions {
  agentId: string | null
  provider: string | null
  model: string | null
  skills: string[]
  sessionId: string | null
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

  // Reset when caller switches sessions externally.
  useEffect(() => { setMessages([]) }, [opts.sessionId])

  const abort = useCallback(() => { abortRef.current?.abort(); abortRef.current = null; setBusy(false) }, [])

  const send = useCallback(async (text: string, images: ImagePart[], mode: ComposerMode = 'chat') => {
    const userMsg: DisplayMessage = {
      id: cryptoId(), kind: 'user', text: mode === 'chat' ? text : `${mode === 'text-to-image' ? '文生图' : '图生图'}：${text}`,
      imageUrls: images.map(i => i.url ?? '').filter(Boolean),
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
    }

    const ctrl = new AbortController()
    abortRef.current = ctrl
    setBusy(true)

    // Track current assistant streaming message id; tool & observation use their own.
    let assistantId: string | null = null
    let toolStreamIds: Record<string, string> = {}

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
        case 'Start':
          assistantId = cryptoId()
          setMessages(prev => [...prev, { id: assistantId!, kind: 'assistant', text: '', streaming: true }])
          break

        case 'TextDelta':
          if (!c.text) return
          if (!assistantId) {
            assistantId = cryptoId()
            setMessages(prev => [...prev, { id: assistantId!, kind: 'assistant', text: c.text!, streaming: true }])
          } else {
            const id = assistantId
            setMessages(prev => prev.map(m => m.id === id ? { ...m, text: m.text + c.text } : m))
          }
          break

        case 'ThinkingDelta':
          if (!c.text) return
          setMessages(prev => {
            const last = prev[prev.length - 1]
            if (last && last.kind === 'thinking' && last.streaming) {
              return prev.map((m, i) => i === prev.length - 1 ? { ...m, text: m.text + c.text } : m)
            }
            return [...prev, { id: cryptoId(), kind: 'thinking', text: c.text!, streaming: true }]
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
          }])
          break
        }
        case 'Warning':
          setMessages(prev => [...prev, { id: cryptoId(), kind: 'warning', text: c.text ?? '' }]); break
        case 'Error':
          setMessages(prev => [...prev, { id: cryptoId(), kind: 'error', text: c.text ?? '' }]); break
        case 'End': /* finalized in finally */ break
        case 'Usage': /* could surface a small footer here */ break
      }
    }
  }, [opts.agentId, opts.provider, opts.model, opts.skills, opts.sessionId, opts.onSession])

  return { messages, busy, send, abort, setMessages }
}

function cryptoId(): string {
  return (globalThis.crypto as Crypto).randomUUID()
}
