import { useEffect, useMemo, useState } from 'react'
import { renderMarkdown, renderUmlDiagrams } from '../services/markdown'

export type DisplayKind = 'user' | 'assistant' | 'tool' | 'thinking' | 'error' | 'warning' | 'observation'

export interface DisplayMessage {
  id: string
  kind: DisplayKind
  text: string
  streaming?: boolean
  imageUrls?: string[]
  toolName?: string
  createdAt?: string
  model?: string | null
  provider?: string | null
  modelSource?: string | null
  agentId?: string | null
  skillId?: string | null
  step?: number | null
  usage?: { input: number; output: number; cacheRead?: number; cacheWrite?: number } | null
}

export function ChatMessageView({ msg }: { msg: DisplayMessage }) {
  useEffect(() => { renderUmlDiagrams() }, [msg.text, msg.streaming])
  const [copied, setCopied] = useState(false)

  const label =
    msg.kind === 'user' ? '你'
    : msg.kind === 'assistant' ? '乾元'
    : msg.kind === 'thinking' ? '思考中…'
    : msg.kind === 'tool' ? `工具调用 · ${msg.toolName ?? ''}`
    : msg.kind === 'observation' ? `工具结果 · ${msg.toolName ?? ''}`
    : msg.kind === 'warning' ? '警告'
    : '错误'

  const role = roleFor(msg.kind)
  const meta = useMemo(() => messageMeta(msg), [msg])

  const cls = msg.kind === 'observation' ? 'observation' : msg.kind
  const isPlain = msg.kind === 'tool' || msg.kind === 'observation'
  const body = isPlain
    ? <PlainBlock text={msg.text || ''} label={msg.kind === 'tool' ? '调用参数' : '返回内容'} />
    : <div className={`body ${msg.streaming ? 'cursor' : ''}`}
           dangerouslySetInnerHTML={{ __html: renderMarkdown(msg.text || '') }} />

  async function copyText() {
    await navigator.clipboard?.writeText(msg.text || '').catch(() => undefined)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1200)
  }

  return (
    <div className={`msg rich-msg ${cls} ${msg.streaming ? 'streaming' : ''}`}>
      <div className="message-avatar" aria-hidden="true">{role.initial}</div>
      <div className="message-card">
        <div className="message-head">
          <div className="message-title-block">
            <div className="who">{label}</div>
            <div className="message-subtitle">{role.subtitle}</div>
          </div>
          <div className="message-actions">
            {meta.map(item => <span key={item} className="message-meta-chip">{item}</span>)}
            {msg.streaming && <span className="live-chip">生成中</span>}
            {msg.text && <button type="button" className="message-copy" onClick={copyText}>{copied ? '已复制' : '复制'}</button>}
          </div>
        </div>
        {msg.imageUrls && msg.imageUrls.length > 0 && (
          <div className="message-images">
            {msg.imageUrls.map((u, i) => <img key={i} src={u} alt="attached" />)}
          </div>
        )}
        {body}
        {msg.usage && <div className="usage-strip">
          <span>输入 {msg.usage.input}</span>
          <span>输出 {msg.usage.output}</span>
          {Boolean(msg.usage.cacheRead) && <span>缓存读 {msg.usage.cacheRead}</span>}
          {Boolean(msg.usage.cacheWrite) && <span>缓存写 {msg.usage.cacheWrite}</span>}
        </div>}
      </div>
    </div>
  )
}

function PlainBlock({ text, label }: { text: string; label: string }) {
  const pretty = tryPrettyJson(text)
  return <details className="tool-detail" open>
    <summary>{label}</summary>
    <pre className="body plain-body">{pretty}</pre>
  </details>
}

function roleFor(kind: DisplayKind) {
  switch (kind) {
    case 'user': return { initial: '你', subtitle: '用户输入' }
    case 'assistant': return { initial: '✦', subtitle: '云端模型回复' }
    case 'thinking': return { initial: '◌', subtitle: '推理过程' }
    case 'tool': return { initial: '⌘', subtitle: '技能与工具' }
    case 'observation': return { initial: '⟡', subtitle: '工具观察结果' }
    case 'warning': return { initial: '⚠', subtitle: '可恢复提示' }
    case 'error': return { initial: '⨯', subtitle: '执行错误' }
  }
}

function messageMeta(msg: DisplayMessage): string[] {
  const meta: string[] = []
  if (msg.createdAt) meta.push(formatTime(msg.createdAt))
  if (msg.modelSource === 'cloud') meta.push('云端')
  if (msg.provider) meta.push(msg.provider)
  if (msg.model) meta.push(msg.model)
  if (msg.agentId) meta.push(msg.agentId)
  if (msg.skillId) meta.push(msg.skillId)
  if (msg.step) meta.push(`Step ${msg.step}`)
  return meta
}

function formatTime(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function tryPrettyJson(s: string): string {
  const t = s.trim()
  if (!t) return s
  if (t.startsWith('{') || t.startsWith('[')) {
    try { return JSON.stringify(JSON.parse(t), null, 2) } catch { /* fall through */ }
  }
  return s
}

