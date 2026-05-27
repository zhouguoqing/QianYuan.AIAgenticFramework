import { renderMarkdown } from '../services/markdown'

export type DisplayKind = 'user' | 'assistant' | 'tool' | 'thinking' | 'error' | 'warning' | 'observation'

export interface DisplayMessage {
  id: string
  kind: DisplayKind
  text: string
  streaming?: boolean
  imageUrls?: string[]
  toolName?: string
}

export function ChatMessageView({ msg }: { msg: DisplayMessage }) {
  const label =
    msg.kind === 'user' ? '你'
    : msg.kind === 'assistant' ? '乾元'
    : msg.kind === 'thinking' ? '思考中…'
    : msg.kind === 'tool' ? `🔧 工具调用 · ${msg.toolName ?? ''}`
    : msg.kind === 'observation' ? `📥 工具结果 · ${msg.toolName ?? ''}`
    : msg.kind === 'warning' ? '警告'
    : '错误'

  const cls = msg.kind === 'observation' ? 'observation' : msg.kind
  const isPlain = msg.kind === 'tool' || msg.kind === 'observation'
  const body = isPlain
    ? <PlainBlock text={msg.text || ''} />
    : <div className={`body ${msg.streaming ? 'cursor' : ''}`}
           dangerouslySetInnerHTML={{ __html: renderMarkdown(msg.text || '') }} />

  return (
    <div className={`msg ${cls}`}>
      <div className="who">{label}</div>
      {msg.imageUrls && msg.imageUrls.length > 0 && (
        <div className="composer images">
          {msg.imageUrls.map((u, i) => <img key={i} src={u} alt="attached" />)}
        </div>
      )}
      {body}
    </div>
  )
}

function PlainBlock({ text }: { text: string }) {
  const pretty = tryPrettyJson(text)
  return <pre className="body plain-body">{pretty}</pre>
}

function tryPrettyJson(s: string): string {
  const t = s.trim()
  if (!t) return s
  if (t.startsWith('{') || t.startsWith('[')) {
    try { return JSON.stringify(JSON.parse(t), null, 2) } catch { /* fall through */ }
  }
  return s
}

