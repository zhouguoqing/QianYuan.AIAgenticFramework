import { useEffect, useRef, useState } from 'react'
import { Sidebar } from './components/Sidebar'
import { Composer } from './components/Composer'
import { ChatMessageView } from './components/ChatMessageView'
import { useChat } from './hooks/useChat'

export default function App() {
  const [agentId, setAgentId] = useState<string | null>(null)
  const [provider, setProvider] = useState<string | null>(null)
  const [model, setModel] = useState<string | null>(null)
  const [skills, setSkills] = useState<string[]>([])
  const [sessionId, setSessionId] = useState<string | null>(null)

  const { messages, busy, send, abort } = useChat({
    agentId, provider, model, skills, sessionId,
    onSession: id => setSessionId(id),
  })

  const scrollerRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const el = scrollerRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [messages])

  return (
    <div className="app">
      <Sidebar
        selectedAgent={agentId} onAgentChange={setAgentId}
        selectedProvider={provider} onProviderChange={setProvider}
        selectedModel={model} onModelChange={setModel}
        selectedSkills={skills} onSkillsChange={setSkills}
        currentSessionId={sessionId}
        onNewSession={() => setSessionId(null)}
        onLoadSession={setSessionId}
      />
      <div className="main">
        <div className="toolbar">
          <span className="tag">Agent: {agentId ?? '默认'}</span>
          <span className="tag">Provider: {provider ?? 'auto'}</span>
          {model && <span className="tag">Model: {model}</span>}
          <span className="tag">Skills: {skills.length === 0 ? '渐进式' : skills.length}</span>
          {busy && <span className="tag"><span className="spinner" /> 生成中</span>}
          <span style={{ flex: 1 }} />
          <span className="tag">Session: {sessionId ?? '新对话'}</span>
        </div>
        <div className="chat" ref={scrollerRef}>
          {messages.length === 0
            ? <div className="empty">
                <h2 style={{ marginTop: 0 }}>QianYuan · 乾元</h2>
                <p>支持 ReAct 推理、渐进式技能加载、多家大模型、图像识别、Web 搜索、MCP、钉钉、流式 WebAPI。</p>
                <p className="small">在左侧选择 Agent 与 Provider，向我提问，我会按需调用工具。</p>
              </div>
            : messages.map(m => <ChatMessageView key={m.id} msg={m} />)}
        </div>
        <Composer busy={busy} onSubmit={send} onAbort={abort} />
      </div>
    </div>
  )
}
