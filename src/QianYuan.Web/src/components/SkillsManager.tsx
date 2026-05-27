import { useEffect, useState } from 'react'
import type {
  SkillManifestDto, SkillToolsResponse, McpStdioRegistrationRequest,
} from '../types/api'
import {
  listSkills, listSkillTools, setSkillEnabled, relevantSkills, registerMcpStdio,
} from '../services/api'

type Tab = 'catalog' | 'relevant' | 'register'

interface Props { onClose: () => void }

export function SkillsManager({ onClose }: Props) {
  const [tab, setTab] = useState<Tab>('catalog')
  const [skills, setSkills] = useState<SkillManifestDto[]>([])

  useEffect(() => { reload() }, [])
  function reload() { listSkills().then(setSkills).catch(() => {}) }

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <strong>技能管理</strong>
          <span style={{ flex: 1 }} />
          <button className="ghost" onClick={onClose}>关闭</button>
        </div>
        <div className="modal-tabs">
          <button className={tab === 'catalog' ? 'active' : ''} onClick={() => setTab('catalog')}>目录</button>
          <button className={tab === 'relevant' ? 'active' : ''} onClick={() => setTab('relevant')}>相关性</button>
          <button className={tab === 'register' ? 'active' : ''} onClick={() => setTab('register')}>注册 MCP</button>
        </div>
        <div className="modal-body">
          {tab === 'catalog' && <CatalogTab skills={skills} onChanged={reload} />}
          {tab === 'relevant' && <RelevantTab />}
          {tab === 'register' && <RegisterMcpTab onRegistered={reload} />}
        </div>
      </div>
    </div>
  )
}

function CatalogTab({ skills, onChanged }: { skills: SkillManifestDto[]; onChanged: () => void }) {
  const [tools, setTools] = useState<Record<string, SkillToolsResponse | null>>({})
  const [expanded, setExpanded] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  async function expand(id: string) {
    if (expanded === id) { setExpanded(null); return }
    setExpanded(id)
    if (tools[id] === undefined) {
      const t = await listSkillTools(id)
      setTools(prev => ({ ...prev, [id]: t }))
    }
  }

  async function toggle(id: string, next: boolean) {
    setBusy(id)
    try {
      await setSkillEnabled(id, next)
      onChanged()
    } finally { setBusy(null) }
  }

  if (skills.length === 0) return <div className="small">暂无技能</div>

  return (
    <div>
      {skills.map(s => {
        const t = tools[s.id]
        return (
          <div key={s.id} className="skill-card">
            <div className="skill-card-head">
              <div style={{ flex: 1, cursor: 'pointer' }} onClick={() => expand(s.id)}>
                <strong>{s.name}</strong>
                <span className="tag" style={{ marginLeft: 8 }}>{s.id}</span>
                {s.requiresNetwork && <span className="tag" style={{ marginLeft: 4 }}>联网</span>}
                {s.requiresFilesystem && <span className="tag" style={{ marginLeft: 4 }}>文件系统</span>}
                <div className="small" style={{ marginTop: 2 }}>{s.description}</div>
              </div>
              <label className="switch" title={s.enabled ? '点击停用' : '点击启用'}>
                <input
                  type="checkbox"
                  checked={s.enabled !== false}
                  disabled={busy === s.id}
                  onChange={e => toggle(s.id, e.target.checked)}
                />
                <span>{s.enabled !== false ? '启用' : '停用'}</span>
              </label>
            </div>
            {expanded === s.id && (
              <div className="skill-tools">
                {t === null && <div className="small">未能加载工具</div>}
                {t === undefined && <div className="small">加载中…</div>}
                {t && (
                  <>
                    {t.systemPromptFragment && (
                      <details>
                        <summary className="small">系统提示片段</summary>
                        <pre>{t.systemPromptFragment}</pre>
                      </details>
                    )}
                    {t.tools.length === 0 && <div className="small">无工具</div>}
                    {t.tools.map(tool => (
                      <details key={tool.name} className="tool">
                        <summary><code>{tool.name}</code> <span className="small">{tool.description}</span></summary>
                        {tool.jsonSchema && <pre>{prettyJson(tool.jsonSchema)}</pre>}
                      </details>
                    ))}
                  </>
                )}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}

function RelevantTab() {
  const [q, setQ] = useState('')
  const [results, setResults] = useState<SkillManifestDto[] | null>(null)
  const [busy, setBusy] = useState(false)

  async function run() {
    if (!q.trim()) return
    setBusy(true)
    try { setResults(await relevantSkills(q.trim())) }
    finally { setBusy(false) }
  }

  return (
    <div>
      <div className="small" style={{ marginBottom: 6 }}>
        预览 ReAct 渐进式选择会启用哪些技能。仅展示，不会真的运行。
      </div>
      <div style={{ display: 'flex', gap: 8 }}>
        <input
          value={q}
          onChange={e => setQ(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter') run() }}
          placeholder="例如:帮我查一下今天的天气"
          style={{ flex: 1 }}
        />
        <button onClick={run} disabled={busy || !q.trim()}>查询</button>
      </div>
      {results && (
        <div style={{ marginTop: 12 }}>
          {results.length === 0 && <div className="small">无匹配</div>}
          {results.map(r => (
            <div key={r.id} className="skill-card">
              <strong>{r.name}</strong>
              <span className="tag" style={{ marginLeft: 8 }}>{r.id}</span>
              <div className="small" style={{ marginTop: 2 }}>{r.description}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function RegisterMcpTab({ onRegistered }: { onRegistered: () => void }) {
  const [serverId, setServerId] = useState('')
  const [command, setCommand] = useState('')
  const [argsText, setArgsText] = useState('')
  const [envText, setEnvText] = useState('')
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<string | null>(null)

  async function submit() {
    setBusy(true); setResult(null)
    try {
      const env: Record<string, string> = {}
      for (const line of envText.split('\n')) {
        const m = line.trim()
        if (!m) continue
        const i = m.indexOf('=')
        if (i > 0) env[m.slice(0, i)] = m.slice(i + 1)
      }
      const req: McpStdioRegistrationRequest = {
        serverId: serverId.trim(),
        command: command.trim(),
        arguments: argsText.split('\n').map(s => s.trim()).filter(Boolean),
        environment: env,
      }
      const r = await registerMcpStdio(req)
      if (r.ok) {
        setResult(`已注册:${r.skillId}`)
        setServerId(''); setCommand(''); setArgsText(''); setEnvText('')
        onRegistered()
      } else {
        setResult(`失败:${r.error}`)
      }
    } finally { setBusy(false) }
  }

  return (
    <div>
      <div className="small" style={{ marginBottom: 8 }}>
        通过 stdio 启动外部 MCP server 并把它的工具挂载为一个技能。仅当前进程内有效;若要持久化请改 appsettings.json 的 McpServers。
      </div>
      <Field label="ServerId">
        <input value={serverId} onChange={e => setServerId(e.target.value)} placeholder="例如:filesystem" />
      </Field>
      <Field label="Command">
        <input value={command} onChange={e => setCommand(e.target.value)} placeholder="例如:npx" />
      </Field>
      <Field label="Arguments (每行一个)">
        <textarea value={argsText} onChange={e => setArgsText(e.target.value)} rows={4}
          placeholder={'-y\n@modelcontextprotocol/server-filesystem\n/tmp'} />
      </Field>
      <Field label="Environment (KEY=VALUE 每行一个)">
        <textarea value={envText} onChange={e => setEnvText(e.target.value)} rows={3} placeholder="" />
      </Field>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 8 }}>
        <button onClick={submit} disabled={busy || !serverId.trim() || !command.trim()}>
          {busy ? '注册中…' : '注册'}
        </button>
        {result && <div className="small">{result}</div>}
      </div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div style={{ marginBottom: 8 }}>
      <div className="small" style={{ marginBottom: 2 }}>{label}</div>
      {children}
    </div>
  )
}

function prettyJson(s: string): string {
  try { return JSON.stringify(JSON.parse(s), null, 2) } catch { return s }
}
