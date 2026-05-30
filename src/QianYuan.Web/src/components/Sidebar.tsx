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
  selectedAgent: string | null
  onAgentChange: (id: string) => void
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
        if (p.selectedProvider == null && pr.defaultProviderId) {
          p.onProviderChange(pr.defaultProviderId)
          const dp = pr.providers.find(x => x.providerId === pr.defaultProviderId)
          if (dp && p.selectedModel == null) p.onModelChange(dp.defaultModel ?? null)
        }
        if (p.selectedAgent == null && a.length > 0) p.onAgentChange(a[0].id)
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

  return (
    <aside className="sidebar">
      <div className="sidebar-brand">
        <span>QianYuan · 乾元</span>
      </div>

      <div className="sidebar-section">
        <button className="primary-btn" onClick={p.onNewSession}>+ 新建会话</button>
      </div>

      <div className="sidebar-section">
        <label className="field-label">Agent</label>
        <select className="field"
          value={p.selectedAgent ?? ''}
          onChange={e => p.onAgentChange(e.target.value)}>
          {agents.length === 0 && <option value="">(无)</option>}
          {agents.map(a => <option key={a.id} value={a.id}>{a.name}</option>)}
        </select>
      </div>

      <div className="sidebar-section">
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

      <div className="sidebar-section">
        <div className="section-head">
          <label className="field-label" style={{ flex: 1 }}>技能</label>
          <button className="ghost-btn" onClick={() => setShowSkillsManager(true)}>管理</button>
        </div>
+        <div style={{ marginTop: 8 }}>
+          <label className="field-label" style={{ flex: 1 }}>知识库</label>
+          <button className="ghost-btn" onClick={() => setShowKnowledgeManager(true)}>管理</button>
+        </div>
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

      <div className="sidebar-section sessions-section">
        <label className="field-label">会话</label>
        <div className="session-list">
          {sessions.length === 0 && <div className="muted-small">暂无</div>}
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

      {showSkillsManager && (
        <SkillsManager onClose={() => { setShowSkillsManager(false); reloadSkills() }} />
      )}
      {showKnowledgeManager && (
        <KnowledgeManager onClose={() => { setShowKnowledgeManager(false); }} />
      )}
    </aside>
  )
}
