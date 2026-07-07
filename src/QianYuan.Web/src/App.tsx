import { useEffect, useRef, useState } from 'react'
import { Sidebar } from './components/Sidebar'
import { Composer } from './components/Composer'
import { ChatMessageView } from './components/ChatMessageView'
import { AgentStore } from './components/AgentStore'
import { AuthPage } from './components/AuthPage'
import { CreditsPanel } from './components/CreditsPanel'
import { WorkTasksPanel } from './components/WorkTasksPanel'
import { useChat } from './hooks/useChat'
import type { AuthResponse } from './types/api'
import { getStoredAuth, logout } from './services/api'

export default function App() {
  const [auth, setAuth] = useState<AuthResponse | null>(() => getStoredAuth())
  const [view, setView] = useState<'chat' | 'agent-store'>('chat')
  const [agentId, setAgentId] = useState<string | null>(null)
  const [provider, setProvider] = useState<string | null>(null)
  const [model, setModel] = useState<string | null>(null)
  const [skills, setSkills] = useState<string[]>([])
  const [sessionId, setSessionId] = useState<string | null>(null)
  const [showCredits, setShowCredits] = useState(false)
  const [showTasks, setShowTasks] = useState(false)

  const { messages, busy, send, abort } = useChat({
    agentId, provider, model, skills, sessionId,
    onSession: id => setSessionId(id),
  })

  const scrollerRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const el = scrollerRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [messages])

  async function signOut() {
    await logout()
    setAuth(null)
    setSessionId(null)
  }

  if (!auth) {
    return <AuthPage onAuthenticated={setAuth} />
  }

  return (
    <div className="app">
      <Sidebar
        onOpenAgentStore={() => setView('agent-store')}
        selectedAgent={agentId} onAgentChange={setAgentId}
        selectedProvider={provider} onProviderChange={setProvider}
        selectedModel={model} onModelChange={setModel}
        selectedSkills={skills} onSkillsChange={setSkills}
        currentSessionId={sessionId}
        onNewSession={() => setSessionId(null)}
        onLoadSession={setSessionId}
      />
      {view === 'agent-store' ? <AgentStore onBack={() => setView('chat')} /> : <div className="main">
        <div className="toolbar">
          <span className="tag">Agent: {agentId ?? '默认'}</span>
          <span className="tag">Provider: {provider ?? 'auto'}</span>
          {model && <span className="tag">Model: {model}</span>}
          <span className="tag">Skills: {skills.length === 0 ? '渐进式' : skills.length}</span>
          {busy && <span className="tag"><span className="spinner" /> 生成中</span>}
          <span style={{ flex: 1 }} />
          <span className="tag">{auth.user.displayName}</span>
          <button className="ghost-btn" onClick={() => setShowTasks(true)}>Tasks</button>
          <button className="ghost-btn" onClick={() => setShowCredits(true)}>Credits</button>
          <button className="ghost-btn" onClick={signOut}>退出</button>
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
      </div>}
      {showTasks && <WorkTasksPanel provider={provider} model={model} onClose={() => setShowTasks(false)} />}
      {showCredits && <CreditsPanel onClose={() => setShowCredits(false)} />}
    </div>
  )
}
