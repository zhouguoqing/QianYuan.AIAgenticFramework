import React, { useEffect, useMemo, useState, useRef } from 'react'
import { renderMarkdown, renderUmlDiagrams, postProcessKatex } from '../services/markdown'

export type DisplayKind = 'user' | 'assistant' | 'tool' | 'thinking' | 'error' | 'warning' | 'observation'

export interface DisplayMessage {
  id: string
  kind: DisplayKind
  text: string
  streaming?: boolean
  imageUrls?: string[]
  toolName?: string
  sourceIndex?: number
  createdAt?: string
  model?: string | null
  provider?: string | null
  modelSource?: string | null
  agentId?: string | null
  skillId?: string | null
  step?: number | null
  usage?: { input: number; output: number; cacheRead?: number; cacheWrite?: number } | null
}

interface ChatMessageViewProps {
  msg: DisplayMessage
  onRegenerateUserMessage?: (msg: DisplayMessage, nextText?: string) => void
}

export const ChatMessageView = React.memo(function ChatMessageView({ msg, onRegenerateUserMessage }: ChatMessageViewProps) {
  const bodyRef = useRef<HTMLDivElement>(null)

  // Render markdown + process KaTeX async
  useEffect(() => {
    renderUmlDiagrams()
    if (bodyRef.current) {
      postProcessKatex(bodyRef.current)
    }
  }, [msg.text, msg.streaming])

  const [copied, setCopied] = useState(false)
  const [thinkingCollapsed, setThinkingCollapsed] = useState(false)

  const label =
    msg.kind === 'user' ? '你'
    : msg.kind === 'assistant' ? '乾元'
    : msg.kind === 'thinking' ? '思考中'
    : msg.kind === 'tool' ? `工具调用 · ${msg.toolName ?? ''}`
    : msg.kind === 'observation' ? `工具结果 · ${msg.toolName ?? ''}`
    : msg.kind === 'warning' ? '警告'
    : '错误'

  const role = roleFor(msg.kind)
  const meta = useMemo(() => messageMeta(msg), [msg])
  const canRegenerate = msg.kind === 'user' && !msg.streaming && msg.sourceIndex !== undefined && Boolean(onRegenerateUserMessage)

  const cls = msg.kind === 'observation' ? 'observation' : msg.kind
  const isPlain = msg.kind === 'tool' || msg.kind === 'observation'

  const body = isPlain
    ? <PlainBlock text={msg.text || ''} label={msg.kind === 'tool' ? '调用参数' : '返回内容'} />
    : <div
        ref={bodyRef}
        className={`body ${msg.streaming ? 'cursor' : ''}`}
        dangerouslySetInnerHTML={{ __html: renderMarkdown(msg.text || '') }}
      />

  // Thinking messages: collapsible
  const content = msg.kind === 'thinking'
    ? (
      <details className="tool-detail" open={!thinkingCollapsed} onToggle={e => setThinkingCollapsed(!(e.currentTarget as HTMLDetailsElement).open)}>
        <summary>{thinkingCollapsed ? '展开推理过程' : '收起推理过程'}</summary>
        {body}
      </details>
    )
    : body

  async function copyText() {
    await navigator.clipboard?.writeText(msg.text || '').catch(() => undefined)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1200)
  }

  function editAndRegenerate() {
    const next = window.prompt('编辑这条消息后重新生成', msg.text || '')
    if (next !== null) onRegenerateUserMessage?.(msg, next)
  }

  return (
    <div className={`rich-msg ${cls} ${msg.streaming ? 'streaming' : ''}`}>
      <div className="message-avatar" aria-hidden="true">{role.initial}</div>
      <div className="message-card">
        <div className="message-head">
          <div className="message-title-block">
            <div className="who">
              <span className="who-icon">{role.emoji}</span>
              {label}
            </div>
            <div className="message-subtitle">{role.subtitle}</div>
          </div>
          <div className="message-actions">
            {meta.map(item => <span key={item} className="message-meta-chip">{item}</span>)}
            {msg.streaming && <span className="live-chip">生成中</span>}
            {canRegenerate && <button type="button" className="message-copy" onClick={() => onRegenerateUserMessage?.(msg)}>🔄 重新生成</button>}
            {canRegenerate && <button type="button" className="message-copy" onClick={editAndRegenerate}>✏️ 编辑重发</button>}
            {msg.text && <button type="button" className="message-copy" onClick={copyText}>{copied ? '✓ 已复制' : '📋 复制'}</button>}
          </div>
        </div>
        {msg.imageUrls && msg.imageUrls.length > 0 && (
          <div className="message-images">
            {msg.imageUrls.map((u, i) => <img key={i} src={u} alt="attached" loading="lazy" />)}
          </div>
        )}
        {content}
        {msg.usage && <div className="usage-strip">
          <span>📥 输入 {msg.usage.input}</span>
          <span>📤 输出 {msg.usage.output}</span>
          {Boolean(msg.usage.cacheRead) && <span>💾 缓存读 {msg.usage.cacheRead}</span>}
          {Boolean(msg.usage.cacheWrite) && <span>📝 缓存写 {msg.usage.cacheWrite}</span>}
        </div>}
      </div>
    </div>
  )
})

function PlainBlock({ text, label }: { text: string; label: string }) {
  const pretty = tryPrettyJson(text)
  return <details className="tool-detail" open>
    <summary>{label}</summary>
    <pre className="plain-body">{pretty}</pre>
  </details>
}

function roleFor(kind: DisplayKind) {
  switch (kind) {
    case 'user': return { initial: '我', emoji: '👤', subtitle: '用户消息' }
    case 'assistant': return { initial: '乾', emoji: '🤖', subtitle: 'AI 回复' }
    case 'thinking': return { initial: '思', emoji: '💭', subtitle: '推理过程' }
    case 'tool': return { initial: '调', emoji: '🔧', subtitle: '工具调用' }
    case 'observation': return { initial: '结', emoji: '📋', subtitle: '工具返回' }
    case 'warning': return { initial: '!', emoji: '⚠️', subtitle: '运行警告' }
    case 'error': return { initial: '错', emoji: '❌', subtitle: '运行错误' }
  }
}

function messageMeta(msg: DisplayMessage): string[] {
  const meta: string[] = []
  if (msg.createdAt) meta.push(formatTime(msg.createdAt))
  if (msg.provider) {
    const label = msg.modelSource ? `${msg.provider} · ${msg.modelSource}` : msg.provider
    meta.push(label)
  }
  if (msg.model && msg.model !== 'auto') meta.push(msg.model)
  if (msg.agentId) meta.push(`Agent: ${msg.agentId}`)
  if (msg.skillId) meta.push(`Skill: ${msg.skillId}`)
  if (msg.step != null) meta.push(`Step ${msg.step}`)
  return meta
}

function formatTime(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
  } catch { return '' }
}

function tryPrettyJson(text: string): string {
  try {
    const parsed = JSON.parse(text)
    return JSON.stringify(parsed, null, 2)
  } catch { return text }
}
