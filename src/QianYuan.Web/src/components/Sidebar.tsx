import { useEffect, useState } from 'react'
import type {
  AgentDto, ProviderDto, SkillManifestDto, SessionSummaryDto,
} from '../types/api'
import {
  listAgents, listProviders, listSkills, listSessions, deleteSession, updateSession, exportSession, clearSessions,
} from '../services/api'
import { SkillsManager } from './SkillsManager'
import { KnowledgeManager } from './KnowledgeManager'

interface Props {
  onOpenAgentStore: () => void
  onOpenExperts: () => void
  onOpenAccountMenu: () => void
  userName?: string
  selectedAgent: string | null
  onAgentChange: (id: string | null) => void
  selectedProvider: string | null
  onProviderChange: (id: string | null) => void
  selectedModel: string | null
  onModelChange: (m: string | null) => void
  selectedSkills: string[]
  onSkillsChange: (s: string[]) => void
  currentSessionId: string | null
  onNewSession: () => void | Promise<void>
  sessionListVersion?: number
  onLoadSession: (id: string) => void
  onSessionsCleared?: () => void
}

export function Sidebar(p: Props) {
  const [agents, setAgents] = useState<AgentDto[]>([])
  const [providers, setProviders] = useState<ProviderDto[]>([])
  const [defaultProviderId, setDefaultProviderId] = useState<string | null>(null)
  const [skills, setSkills] = useState<SkillManifestDto[]>([])
  const [sessions, setSessions] = useState<SessionSummaryDto[]>([])
  const [sessionQuery, setSessionQuery] = useState('')
  const [openSessionMenuId, setOpenSessionMenuId] = useState<string | null>(null)
  const [showSkillsManager, setShowSkillsManager] = useState(false)
  const [showKnowledgeManager, setShowKnowledgeManager] = useState(false)

  useEffect(() => {
    Promise.all([listAgents(), listProviders(), listSkills(), listSessions(sessionQuery)])
      .then(([a, pr, s, ss]) => {
        setAgents(a)
        setProviders(pr.providers)
        setDefaultProviderId(pr.defaultProviderId)
        setSkills(s)
        setSessions(ss)
      })
      .catch(console.error)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const t = setInterval(() => reloadSessions(), 5000)
    return () => clearInterval(t)
  }, [sessionQuery])

  useEffect(() => { reloadSessions() }, [sessionQuery])

  useEffect(() => { reloadSessions() }, [p.sessionListVersion])
  useEffect(() => { setOpenSessionMenuId(null) }, [sessionQuery, p.sessionListVersion])
  useEffect(() => {
    if (!openSessionMenuId) return
    const closeSessionMenu = () => setOpenSessionMenuId(null)
    document.addEventListener('click', closeSessionMenu)
    return () => document.removeEventListener('click', closeSessionMenu)
  }, [openSessionMenuId])

  function toggleSkill(id: string) {
    p.onSkillsChange(p.selectedSkills.includes(id)
      ? p.selectedSkills.filter(x => x !== id)
      : [...p.selectedSkills, id])
  }

  function pickProvider(id: string | null) {
    p.onProviderChange(id)
    if (id == null) { p.onModelChange(null); return }
    const pr = providers.find(x => x.providerId === id)
    p.onModelChange(pr?.defaultModel ?? null)
  }

  function reloadSkills() { listSkills().then(setSkills).catch(() => {}) }
  function reloadSessions() { listSessions(sessionQuery).then(setSessions).catch(() => {}) }
  async function renameSession(id: string, currentTitle?: string | null) {
    const next = window.prompt('重命名会话', sanitizeSessionTitle(currentTitle) ?? '')
    if (next === null) return
    await updateSession(id, { title: next.trim() || null })
    setOpenSessionMenuId(null)
    await reloadSessions()
  }
  async function removeSession(id: string) {
    if (!window.confirm('确定删除这个会话吗？')) return
    await deleteSession(id)
    setOpenSessionMenuId(null)
    await reloadSessions()
  }
  async function clearAllSessions() {
    if (sessions.length === 0) return
    if (!window.confirm(`确定清空全部 ${sessions.length} 条会话记录吗？此操作不可恢复。`)) return
    setOpenSessionMenuId(null)
    setSessions([])
    p.onSessionsCleared?.()
    try {
      await clearSessions()
    } catch (err) {
      window.alert(`清空会话记录失败：${String(err instanceof Error ? err.message : err)}`)
    } finally {
      await reloadSessions()
    }
  }

  async function downloadSession(id: string, format: 'markdown' | 'json') {
    const { blob, filename } = await exportSession(id, format)
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = filename
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
    setOpenSessionMenuId(null)
  }

  const selectedProviderModels =
    providers.find(x => x.providerId === p.selectedProvider)?.models ?? []

  const navItems = [
    { label: '新建会话', icon: '+', action: p.onNewSession, active: true },
    { label: '助手', icon: 'A' },
    { label: '项目', icon: 'P' },
    { label: '专家·技能·连接器', icon: 'S', action: p.onOpenExperts },
    { label: '自动化', icon: 'R' },
    { label: '更多', icon: 'M' },
  ]

  const fallbackTasks = [
    { title: '研究云快充切换特来电...', time: '17小时前', fresh: false },
    { title: 'AI WorkPartner版本区别对比', time: '2天前', fresh: false },
    { title: 'QQ音乐耳机播放问题排查', time: '2天前', fresh: false },
    { title: '对比能源管理平台产品', time: '', fresh: true },
    { title: '下载山东高考英语真题答案', time: '6天前', fresh: false },
    { title: '询问专家组能力', time: '6天前', fresh: false },
  ]

  return (
    <aside className="sidebar">
      <div className="sidebar-nav">
        {navItems.map(item => (
          <button key={item.label} className={`nav-item ${item.active ? 'active' : ''}`} onClick={() => { void item.action?.() }} type="button">
            <span className="nav-icon">{item.icon}</span>
            <span>{item.label}</span>
            {item.label === '更多' && <em>资料库·灵感</em>}
          </button>
        ))}
      </div>

      <details className="runtime-settings">
        <summary>运行设置</summary>
        <div className="sidebar-section compact">
          <label className="field-label">Agent</label>
          <select className="field"
            value={p.selectedAgent ?? ''}
            onChange={e => p.onAgentChange(e.target.value || null)}>
            <option value="">选择Agent</option>
            {agents.map(a => <option key={a.id} value={a.id}>{a.name}</option>)}
          </select>
        </div>

        <div className="sidebar-section compact">
          <label className="field-label">Provider</label>
          <select className="field"
            value={p.selectedProvider ?? ''}
            onChange={e => pickProvider(e.target.value || null)}>
            <option value="">Agent 默认</option>
            {providers.map(pr => (
              <option key={pr.providerId} value={pr.providerId}>
                {pr.providerId}{pr.providerId === defaultProviderId ? ' ★' : ''}
              </option>
            ))}
          </select>

          {p.selectedProvider && (
            <>
              <label className="field-label" style={{ marginTop: 8 }}>模型</label>
              <select className="field"
                value={p.selectedModel ?? ''}
                onChange={e => p.onModelChange(e.target.value || null)}>
                {selectedProviderModels.length === 0 && <option value="">(无可选)</option>}
                {selectedProviderModels.map(m => <option key={m} value={m}>{m}</option>)}
              </select>
            </>
          )}
        </div>

        <div className="sidebar-section compact">
          <div className="section-head">
            <label className="field-label" style={{ flex: 1 }}>技能</label>
            <button className="ghost-btn" onClick={() => setShowSkillsManager(true)}>管理</button>
          </div>
          <div className="section-head knowledge-entry">
            <label className="field-label" style={{ flex: 1 }}>知识库</label>
            <button className="ghost-btn" onClick={() => setShowKnowledgeManager(true)}>管理</button>
          </div>
          <div className="skill-list">
            {skills.map(s => (
              <label key={s.id}
                className={`skill-item ${s.enabled === false ? 'disabled' : ''}`}
                title={s.description}>
                <input
                  type="checkbox"
                  checked={p.selectedSkills.includes(s.id)}
                  onChange={() => toggleSkill(s.id)}
                />
                <span className="skill-name">{s.name}</span>
              </label>
            ))}
            {skills.length === 0 && <div className="muted-small">暂无</div>}
          </div>
          <div className="hint-small">未勾选时由 ReAct 渐进式加载</div>
        </div>
      </details>

      <div className="sidebar-section sessions-section">
        <div className="session-toolbar">
          <label className="field-label">会话 ({sessions.length || fallbackTasks.length})</label>
          <button className="ghost-btn session-clear-btn" type="button" onClick={() => { void clearAllSessions() }} disabled={sessions.length === 0}>清空会话记录</button>
        </div>
        <input className="field session-search" value={sessionQuery} onChange={e => setSessionQuery(e.target.value)} placeholder="搜索会话" />
        <div className="session-list">
          {sessions.length === 0 && fallbackTasks.map(task => (
            <div key={task.title} className="session-row placeholder-task">
              <div className="session-title">{task.title}</div>
              <div className="session-meta">
                <span>{task.time || '进行中'}</span>
                {task.fresh && <i />}
              </div>
            </div>
          ))}
          {sessions.map(s => {
            const displayTitle = displaySessionTitle(s)
            const rawTitle = s.title?.trim()
            const repaired = Boolean(rawTitle) && rawTitle !== displayTitle
            return (
              <div key={s.sessionId}
                className={`session-row ${s.sessionId === p.currentSessionId ? 'active' : ''}`}
                onClick={() => {
                  setOpenSessionMenuId(null)
                  p.onLoadSession(s.sessionId)
                }}
                title={`${displayTitle}${repaired ? `\n原始标题：${rawTitle}` : ''}\n${new Date(s.updatedAt).toLocaleString()}`}>
                <div className="session-row-main">
                  <div className="session-copy">
                    <div className="session-title">{displayTitle}</div>
                    <div className="session-subtitle">{s.messageCount} 条 · {new Date(s.updatedAt).toLocaleString([], { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })}</div>
                  </div>
                  <div className="session-actions">
                    <div className="session-more-wrap">
                      <button className="session-icon-btn" type="button" aria-label="更多" title="更多"
                        onClick={e => {
                          e.stopPropagation()
                          setOpenSessionMenuId(openSessionMenuId === s.sessionId ? null : s.sessionId)
                        }}>…</button>
                      {openSessionMenuId === s.sessionId && <div className="session-more-menu" onClick={e => e.stopPropagation()}>
                        <button type="button" onClick={() => { void downloadSession(s.sessionId, 'markdown') }}>导出</button>
                        <button type="button" onClick={() => { void downloadSession(s.sessionId, 'json') }}>JSON</button>
                      </div>}
                    </div>
                    <button className="session-action-btn" type="button" aria-label="删除" title="删除"
                      onClick={e => {
                        e.stopPropagation()
                        void removeSession(s.sessionId)
                      }}>删除</button>
                    <button className="session-action-btn" type="button" aria-label="重命名" title="重命名"
                      onClick={e => {
                        e.stopPropagation()
                        void renameSession(s.sessionId, s.title)
                      }}>重命名</button>
                  </div>
                </div>
              </div>
            )
          })}
        </div>
      </div>

      <div className="sidebar-footer">
        <button className="sidebar-user-button" type="button" onClick={p.onOpenAccountMenu} title="打开用户菜单">
          <span className="avatar-mark">{(p.userName ?? '访客').slice(0, 1).toUpperCase()}</span>
          <strong>{p.userName ?? '访客'}</strong>
        </button>
        <button className="footer-icon" type="button" aria-label="通知">!</button>
        <button className="footer-icon" type="button" aria-label="链接">↗</button>
      </div>

      {showSkillsManager && (
        <SkillsManager onClose={() => { setShowSkillsManager(false); reloadSkills() }} />
      )}
      {showKnowledgeManager && (
        <KnowledgeManager provider={p.selectedProvider} onClose={() => { setShowKnowledgeManager(false); }} />
      )}
    </aside>
  )

function displaySessionTitle(session: SessionSummaryDto): string {
  const title = sanitizeSessionTitle(session.title)
  if (title) return title

  const updatedAt = new Date(session.updatedAt)
  const time = Number.isNaN(updatedAt.getTime())
    ? '未命名'
    : updatedAt.toLocaleString([], { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
  return `历史会话 · ${time}`
}

function sanitizeSessionTitle(title?: string | null): string | null {
  const value = title?.trim()
  if (!value) return null

  const compact = value.replace(/\s/g, '')
  const questionCount = compact.split('').filter(ch => ch === '?').length
  const readableCount = (compact.match(/[A-Za-z0-9\u4e00-\u9fff]/g) ?? []).length - questionCount
  const questionRatio = compact.length > 0 ? questionCount / compact.length : 0

  if (questionCount >= 2 && (compact.length <= 4 || questionRatio >= 0.45 || readableCount <= 2)) return null
  return value
}
}