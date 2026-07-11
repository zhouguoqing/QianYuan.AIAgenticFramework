import { useEffect, useState } from 'react'
import type {
  AgentDto, ProviderDto, SkillManifestDto, SessionSummaryDto,
} from '../types/api'
import {
  listAgents, listProviders, listSkills, listSessions, deleteSession,
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
  onNewSession: () => void
  onLoadSession: (id: string) => void
}

export function Sidebar(p: Props) {
  const [agents, setAgents] = useState<AgentDto[]>([])
  const [providers, setProviders] = useState<ProviderDto[]>([])
  const [defaultProviderId, setDefaultProviderId] = useState<string | null>(null)
  const [skills, setSkills] = useState<SkillManifestDto[]>([])
  const [sessions, setSessions] = useState<SessionSummaryDto[]>([])
  const [showSkillsManager, setShowSkillsManager] = useState(false)
  const [showKnowledgeManager, setShowKnowledgeManager] = useState(false)

  useEffect(() => {
    Promise.all([listAgents(), listProviders(), listSkills(), listSessions()])
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
    const t = setInterval(() => listSessions().then(setSessions).catch(() => {}), 5000)
    return () => clearInterval(t)
  }, [])

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

  const selectedProviderModels =
    providers.find(x => x.providerId === p.selectedProvider)?.models ?? []

  const navItems = [
    { label: '新建会话', icon: '+', action: p.onNewSession, active: true },
    { label: '助理', icon: 'A' },
    { label: '专家', icon: 'E', action: p.onOpenExperts },
    { label: '项目', icon: 'P' },
    { label: '专家·技能·连接器', icon: 'S', action: p.onOpenAgentStore },
    { label: '自动化', icon: 'R' },
    { label: '更多', icon: 'M' },
  ]

  const fallbackTasks = [
    { title: '研究云快充切换特来电充...', time: '17小时前', fresh: false },
    { title: 'AI WorkPartner版本区别对比', time: '2天前', fresh: false },
    { title: 'QQ音乐耳机播放问题排查', time: '2天前', fresh: false },
    { title: '对比能源管理平台产品', time: '', fresh: true },
    { title: '下载山东高考英语真题答案', time: '6天前', fresh: false },
    { title: '询问专家团能力', time: '6天前', fresh: false },
  ]

  return (
    <aside className="sidebar">
      <div className="sidebar-nav">
        {navItems.map(item => (
          <button key={item.label} className={`nav-item ${item.active ? 'active' : ''}`} onClick={item.action} type="button">
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
            <option value="">默认助理</option>
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
        <label className="field-label">任务 ({sessions.length || fallbackTasks.length})</label>
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
          {sessions.map(s => (
            <div key={s.sessionId}
              className={`session-row ${s.sessionId === p.currentSessionId ? 'active' : ''}`}
              onClick={() => p.onLoadSession(s.sessionId)}
              title={`${s.title ?? ''}\n${new Date(s.updatedAt).toLocaleString()}`}>
              <div className="session-title">{s.title ?? '(未命名)'}</div>
              <div className="session-meta">
                <span>{s.messageCount} 条</span>
                <button className="link-btn"
                  onClick={e => {
                    e.stopPropagation()
                    deleteSession(s.sessionId).then(() => listSessions().then(setSessions))
                  }}>删除</button>
              </div>
            </div>
          ))}
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
}
