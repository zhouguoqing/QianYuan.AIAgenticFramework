import { useEffect, useRef, useState } from 'react'
import { Sidebar } from './components/Sidebar'
import { Composer } from './components/Composer'
import { ChatMessageView, type DisplayMessage } from './components/ChatMessageView'
import { AgentStore } from './components/AgentStore'
import { ExpertMarketplace } from './components/ExpertMarketplace'
import { AuthPage } from './components/AuthPage'
import { CreditsPanel } from './components/CreditsPanel'
import { WorkTasksPanel } from './components/WorkTasksPanel'
import { AccountMenu } from './components/AccountMenu'
import { useChat } from './hooks/useChat'
import type { AuthResponse, ComposerMode, ImageGenerationOptions, ImagePart, ExpertDetailDto, WorkspaceContext } from './types/api'
import { createSession, getExpertPrompt, getMe, getStoredAuth, logout, storeAuth } from './services/api'

type ActiveExpert = { id: string; name: string; avatarUrl: string; profession: string; systemPrompt: string; boundAgentId?: string | null }

export default function App() {
  const [auth, setAuth] = useState<AuthResponse | null>(() => getStoredAuth())
  const [theme, setTheme] = useState<'light' | 'dark'>(() => localStorage.getItem('workpartner.theme') === 'dark' ? 'dark' : 'light')
  const [view, setView] = useState<'chat' | 'agent-store' | 'experts'>('chat')
  const [agentId, setAgentId] = useState<string | null>(null)
  const [provider, setProvider] = useState<string | null>(null)
  const [model, setModel] = useState<string | null>(null)
  const [skills, setSkills] = useState<string[]>([])
  const [sessionId, setSessionId] = useState<string | null>(() => localStorage.getItem('workpartner.sessionId'))
  const [sessionListVersion, setSessionListVersion] = useState(0)
  const [showCredits, setShowCredits] = useState(false)
  const [showTasks, setShowTasks] = useState(false)
  const [showAccountMenu, setShowAccountMenu] = useState(false)
  const [accountMenuPlacement, setAccountMenuPlacement] = useState<'topbar' | 'sidebar'>('topbar')
  const [activeExpert, setActiveExpert] = useState<ActiveExpert | null>(null)
  const [composerSeed, setComposerSeed] = useState<{ text: string; nonce: number }>({ text: '', nonce: 0 })
  const [accountNotice, setAccountNotice] = useState<{ title: string; body: string } | null>(null)
  const [authPrompt, setAuthPrompt] = useState<{ reason: string; mode?: 'login' | 'register' } | null>(null)

  const { messages, busy, send, abort, reset, regenerate } = useChat({
    agentId, provider, model, skills, sessionId,
    systemPrompt: activeExpert?.systemPrompt ?? null,
    onSession: id => setSessionId(id),
  })

  const scrollerRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const el = scrollerRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [messages])

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    localStorage.setItem('workpartner.theme', theme)
  }, [theme])

  useEffect(() => {
    if (sessionId) localStorage.setItem('workpartner.sessionId', sessionId)
    else localStorage.removeItem('workpartner.sessionId')
  }, [sessionId])

  useEffect(() => {
    if (!auth) return
    let alive = true
    getMe()
      .then(user => {
        if (!alive) return
        const next = { ...auth, user }
        setAuth(next)
        storeAuth(next)
      })
      .catch(() => undefined)
    return () => { alive = false }
  }, [auth?.accessToken])

  async function signOut() {
    await logout()
    setAuth(null)
    setSessionId(null)
    setActiveExpert(null)
    setShowTasks(false)
    setShowCredits(false)
    setShowAccountMenu(false)
    setAccountNotice(null)
    setView('chat')
  }

  function completeAuth(next: AuthResponse) {
    setAuth(next)
    setAuthPrompt(null)
    setView('chat')
    setShowCredits(false)
    setShowTasks(false)
    setShowAccountMenu(false)
  }

  function requireAuth(reason: string, action?: () => void) {
    if (!auth) {
      setAuthPrompt({ reason })
      return false
    }
    action?.()
    return true
  }

  async function startNewSession() {
    if (!requireAuth('登录或注册后即可新建并保存会话。')) return

    abort()
    reset()
    setActiveExpert(null)
    setComposerSeed(s => (s.text ? { text: '', nonce: s.nonce } : s))
    setView('chat')
    setShowTasks(false)
    setShowCredits(false)

    try {
      const session = await createSession({ title: '新会话', agentId: agentId ?? undefined })
      setSessionId(session.sessionId)
      setSessionListVersion(v => v + 1)
    } catch (err) {
      console.error(err)
      setAccountNotice({ title: '新建会话失败', body: String(err instanceof Error ? err.message : err) })
    }
  }

  function handleSessionsCleared() {
    abort()
    reset()
    setSessionId(null)
    setActiveExpert(null)
    setComposerSeed(s => (s.text ? { text: '', nonce: s.nonce } : s))
    setView('chat')
    setShowTasks(false)
    setShowCredits(false)
    setSessionListVersion(v => v + 1)
  }

  function loadSession(id: string) {
    abort()
    setActiveExpert(null)
    setView('chat')
    setShowTasks(false)
    setShowCredits(false)
    setSessionId(id)
  }

  const hasMessages = messages.length > 0
  const hasActiveChat = hasMessages

  function seedShortcut(text: string) {
    setView('chat')
    setComposerSeed(s => ({ text, nonce: s.nonce + 1 }))
  }

  function guardedSubmit(text: string, images: ImagePart[], mode: ComposerMode, workspace?: WorkspaceContext, imageOptions?: ImageGenerationOptions) {
    if (!requireAuth('登录或注册后即可使用云端大模型、保存会话，并进入 AI 专家组工作台。')) return
    // Clear any pending composer seed so the chat-view composer doesn't re-apply it on mount.
    setComposerSeed(s => (s.text ? { text: '', nonce: s.nonce } : s))
    send(text, images, mode, workspace, imageOptions)
  }

  function regenerateUserMessage(message: DisplayMessage, nextText?: string) {
    if (message.sourceIndex === undefined) return
    regenerate(message.sourceIndex, nextText ?? message.text)
  }

  function openAccountMenu(placement: 'topbar' | 'sidebar') {
    setAccountMenuPlacement(placement)
    setShowAccountMenu(true)
  }

  async function summonExpert(prompt: string, expert: ExpertDetailDto) {
    setView('chat')
    setAgentId(expert.boundAgentId ?? null)
    setComposerSeed(s => ({ text: prompt, nonce: s.nonce + 1 }))
    const base: ActiveExpert = {
      id: expert.id, name: expert.name, avatarUrl: expert.avatarUrl,
      profession: expert.profession,
      boundAgentId: expert.boundAgentId,
      systemPrompt: `你是${expert.name}，一位${expert.profession}。${expert.description}`,
    }
    setActiveExpert(base)
    try {
      const { systemPrompt, boundAgentId } = await getExpertPrompt(expert.id)
      const next = { ...base, systemPrompt: systemPrompt || base.systemPrompt, boundAgentId: boundAgentId ?? base.boundAgentId }
      setActiveExpert(next)
      setAgentId(next.boundAgentId ?? null)
    } catch { /* keep fallback persona */ }
  }

  return (
    <div className="app">
      <Sidebar
        onOpenAgentStore={() => requireAuth('登录后可以管理专家、技能和连接器。', () => setView('agent-store'))}
        onOpenExperts={() => requireAuth('登录后可以浏览并召唤专家。', () => setView('experts'))}
        onOpenAccountMenu={() => auth ? openAccountMenu('sidebar') : setAuthPrompt({ reason: '登录或注册后继续使用。' })}
        userName={auth?.user.displayName ?? '访客'}
        selectedAgent={agentId} onAgentChange={setAgentId}
        selectedProvider={provider} onProviderChange={setProvider}
        selectedModel={model} onModelChange={setModel}
        selectedSkills={skills} onSkillsChange={setSkills}
        currentSessionId={sessionId}
        onNewSession={startNewSession}
        onLoadSession={loadSession}
        sessionListVersion={sessionListVersion}
        onSessionsCleared={handleSessionsCleared}
      />
      {view === 'agent-store' ? <AgentStore onBack={() => setView('chat')} />
        : view === 'experts' ? <ExpertMarketplace
          onBack={() => setView('chat')}
          onLaunch={summonExpert}
        />
        : <div className="main">
        {hasActiveChat ? <>
          <div className="chat" ref={scrollerRef}>
            {messages.map(m => <ChatMessageView key={m.id} msg={m} onRegenerateUserMessage={regenerateUserMessage} />)}
          </div>
          <Composer
            busy={busy}
            onSubmit={guardedSubmit}
            onAbort={abort}
            selectedAgent={agentId}
            onAgentChange={setAgentId}
            selectedProvider={provider}
            onProviderChange={setProvider}
            selectedModel={model}
            onModelChange={setModel}
            selectedSkills={skills}
            onSkillsChange={setSkills}
            activeExpert={activeExpert}
            onClearExpert={() => setActiveExpert(null)}
            seedText={composerSeed.text}
            seedNonce={composerSeed.nonce}
          />
        </> : <HomeLanding
          busy={busy}
          onSubmit={guardedSubmit}
          onAbort={abort}
          onShortcut={seedShortcut}
          selectedAgent={agentId}
          onAgentChange={setAgentId}
          selectedProvider={provider}
          onProviderChange={setProvider}
          selectedModel={model}
          onModelChange={setModel}
          selectedSkills={skills}
          onSkillsChange={setSkills}
          activeExpert={activeExpert}
          onClearExpert={() => setActiveExpert(null)}
          seedText={composerSeed.text}
          seedNonce={composerSeed.nonce}
        />}
      </div>}
      {showTasks && auth && <WorkTasksPanel provider={provider ?? undefined} model={model ?? undefined} onClose={() => setShowTasks(false)} />}
      {showCredits && auth && <CreditsPanel onClose={() => setShowCredits(false)} />}
      {showAccountMenu && auth && <AccountMenu
        user={auth.user}
        theme={theme}
        placement={accountMenuPlacement}
        onThemeChange={setTheme}
        onOpenCredits={() => { setShowAccountMenu(false); setShowCredits(true) }}
        onOpenGrowthPlan={() => { setShowAccountMenu(false); setShowTasks(true) }}
        onOpenSettings={() => { setShowAccountMenu(false); setAccountNotice({ title: '设置', body: '运行设置已在左侧栏提供，可配置 Agent、Provider、模型、技能和知识库。' }) }}
        onOpenHelp={() => { setShowAccountMenu(false); setAccountNotice({ title: '帮助与反馈', body: '请把使用问题、改进建议或异常现象记录下来，当前本地版本会优先保留你的会话和任务上下文。' }) }}
        onCheckUpdates={() => { setShowAccountMenu(false); setAccountNotice({ title: '检查更新', body: '当前版本为 WorkPartner v0.1.0。本地开发版更新随仓库代码同步。' }) }}
        onSignOut={signOut}
        onClose={() => setShowAccountMenu(false)}
      />}
      {accountNotice && <div className="modal-backdrop notice-backdrop" onClick={() => setAccountNotice(null)}>
        <div className="modal account-notice" onClick={event => event.stopPropagation()}>
          <div className="modal-header">
            <strong>{accountNotice.title}</strong>
            <span style={{ flex: 1 }} />
            <button className="ghost" onClick={() => setAccountNotice(null)}>关闭</button>
          </div>
          <div className="modal-body">
            <p>{accountNotice.body}</p>
          </div>
        </div>
      </div>}
      {authPrompt && <AuthPage
        reason={authPrompt.reason}
        initialMode={authPrompt.mode ?? 'login'}
        onAuthenticated={completeAuth}
        onCancel={() => setAuthPrompt(null)}
      />}
    </div>
  )
}


interface HomeLandingProps {
  busy: boolean
  onSubmit: (text: string, images: ImagePart[], mode: ComposerMode, workspace?: WorkspaceContext, imageOptions?: ImageGenerationOptions) => void
  onAbort: () => void
  onShortcut: (text: string) => void
  selectedAgent: string | null
  onAgentChange: (id: string | null) => void
  selectedProvider: string | null
  onProviderChange: (id: string | null) => void
  selectedModel: string | null
  onModelChange: (model: string | null) => void
  selectedSkills: string[]
  onSkillsChange: (skills: string[]) => void
  activeExpert: ActiveExpert | null
  onClearExpert: () => void
  seedText: string
  seedNonce: number
}

function HomeLanding({
  busy, onSubmit, onAbort, onShortcut,
  selectedAgent, onAgentChange,
  selectedProvider, onProviderChange,
  selectedModel, onModelChange,
  selectedSkills, onSkillsChange,
  activeExpert, onClearExpert, seedText, seedNonce,
}: HomeLandingProps) {
  const [activeModeLabel, setActiveModeLabel] = useState('代码开发')

  const workModes = [
    { label: '日常办公', prompt: '帮我整理今天的工作事项，并给出可执行的优先级计划。' },
    { label: '代码开发', prompt: '帮我分析这个代码需求，拆解实现步骤并指出风险点。' },
    { label: '设计创意', prompt: '帮我生成一个产品功能的界面方案，包含布局、交互和文案。' },
  ]
  const taskTypes = [
    { label: '文档处理', prompt: '帮我把这份材料整理成结构化文档。' },
    { label: '金融服务', prompt: '帮我梳理这个金融业务场景的关键指标和分析框架。' },
    { label: '高考我帮你', prompt: '帮我设计一份高考志愿咨询的信息收集表和分析流程。' },
    { label: '更多', prompt: '根据我的目标，帮我推荐最合适的工作模式。' },
  ]

  return (
    <main className="home-stage">
      <section className="home-hero">
        <div className="hero-copy">
          <h1>AI WorkPartner<br />你的 AI 智能伙伴</h1>
          <div className="home-mode-tabs" aria-label="常用工作模式">
            {workModes.map(item => (
              <button
                key={item.label}
                className={item.label === activeModeLabel ? 'active' : ''}
                onClick={() => {
                  setActiveModeLabel(item.label)
                  onShortcut(item.prompt)
                }}>
                {item.label}
              </button>
            ))}
          </div>
        </div>
      </section>

      <div className="quick-task-row">
        {taskTypes.map(item => (
          <button key={item.label} onClick={() => onShortcut(item.prompt)}>{item.label}</button>
        ))}
      </div>

      <div className="home-composer-shell">
        <Composer
          busy={busy}
          onSubmit={onSubmit}
          onAbort={onAbort}
          selectedAgent={selectedAgent}
          onAgentChange={onAgentChange}
          selectedProvider={selectedProvider}
          onProviderChange={onProviderChange}
          selectedModel={selectedModel}
          onModelChange={onModelChange}
          selectedSkills={selectedSkills}
          onSkillsChange={onSkillsChange}
          activeExpert={activeExpert}
          onClearExpert={onClearExpert}
          seedText={seedText}
          seedNonce={seedNonce}
        />
      </div>

      <div className="buddy-bot" aria-hidden="true">
        <div className="bot-ear left" />
        <div className="bot-ear right" />
        <div className="bot-face"><span /> <span /></div>
      </div>
    </main>
  )
}


