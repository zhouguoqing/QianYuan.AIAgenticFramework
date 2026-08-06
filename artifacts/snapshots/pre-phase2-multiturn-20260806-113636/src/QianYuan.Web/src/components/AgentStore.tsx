import { useEffect, useMemo, useState } from 'react'
import type {
  AgentStoreAgentDto, AgentStoreToolDto, ProviderDto, SkillManifestDto,
} from '../types/api'
import {
  addAgentStoreCliService, addAgentStoreMcpServer, addAgentStoreSkill,
  createAgentStoreAgent, deleteAgentStoreAgent, getAgentStoreAgent,
  interactAgentStore, listAgentStoreAgents, listAgentStoreTools,
  listProviders, listSkills, removeAgentStoreCliService,
  removeAgentStoreMcpServer, removeAgentStoreSkill, testAgentStoreTool,
  updateAgentStoreAgent,
} from '../services/api'

type Tab = 'profile' | 'skills' | 'mcp' | 'cli' | 'test'

type AgentForm = {
  id: string
  name: string
  description: string
  defaultProviderId: string
  defaultModel: string
  systemPrompt: string
}

const blankAgent: AgentForm = {
  id: '',
  name: '',
  description: '',
  defaultProviderId: '',
  defaultModel: '',
  systemPrompt: '',
}

export function AgentStore({ onBack }: { onBack: () => void }) {
  const [agents, setAgents] = useState<AgentStoreAgentDto[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [selected, setSelected] = useState<AgentStoreAgentDto | null>(null)
  const [providers, setProviders] = useState<ProviderDto[]>([])
  const [skills, setSkills] = useState<SkillManifestDto[]>([])
  const [tab, setTab] = useState<Tab>('profile')
  const [form, setForm] = useState<AgentForm>(blankAgent)
  const [isNew, setIsNew] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  async function reload(nextId = selectedId) {
    const [agentRows, providerRows, skillRows] = await Promise.all([
      listAgentStoreAgents(),
      listProviders(),
      listSkills(),
    ])
    setAgents(agentRows)
    setProviders(providerRows.providers)
    setSkills(skillRows)
    const id = nextId ?? agentRows[0]?.id ?? null
    setSelectedId(id)
    if (id) {
      const detail = await getAgentStoreAgent(id)
      setSelected(detail)
      setForm(fromAgent(detail))
      setIsNew(false)
    } else {
      setSelected(null)
      setForm(blankAgent)
      setIsNew(true)
    }
  }

  useEffect(() => {
    reload().catch(err => setError(String(err.message ?? err)))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function run(action: () => Promise<void>, success?: string) {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await action()
      if (success) setNotice(success)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  function startNew() {
    setSelectedId(null)
    setSelected(null)
    setForm(blankAgent)
    setIsNew(true)
    setTab('profile')
    setError(null)
    setNotice(null)
  }

  async function selectAgent(id: string) {
    setBusy(true)
    setError(null)
    try {
      const detail = await getAgentStoreAgent(id)
      setSelectedId(id)
      setSelected(detail)
      setForm(fromAgent(detail))
      setIsNew(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  async function saveAgent() {
    await run(async () => {
      const payload = { ...form }
      const saved = isNew
        ? await createAgentStoreAgent(payload)
        : await updateAgentStoreAgent(selectedId ?? form.id, payload)
      await reload(saved.id)
    }, isNew ? 'Agent 已创建' : 'Agent 已保存')
  }

  async function removeAgent() {
    if (!selectedId) return
    await run(async () => {
      await deleteAgentStoreAgent(selectedId)
      await reload(null)
    }, 'Agent 已删除')
  }

  const providerModels = useMemo(
    () => providers.find(p => p.providerId === form.defaultProviderId)?.models ?? [],
    [form.defaultProviderId, providers],
  )

  return (
    <div className="agent-store-shell">
      <aside className="agent-store-list">
        <div className="agent-store-list-head">
          <button className="secondary-btn" onClick={onBack}>返回对话</button>
          <button className="primary-inline-btn" onClick={startNew}>新建 Agent</button>
        </div>
        <div className="agent-store-title">Agent Store</div>
        <div className="agent-store-count">{agents.length} 个 Agent</div>
        <div className="agent-store-agent-list">
          {agents.map(agent => (
            <button
              key={agent.id}
              className={`agent-store-agent-row ${agent.id === selectedId ? 'active' : ''}`}
              onClick={() => selectAgent(agent.id)}
              type="button">
              <span>{agent.name}</span>
              <small>{agent.id}</small>
            </button>
          ))}
          {agents.length === 0 && !isNew && <div className="agent-store-empty">暂无 Agent</div>}
        </div>
      </aside>

      <main className="agent-store-main">
        <div className="agent-store-topbar">
          <div>
            <h1>{isNew ? '新建 Agent' : selected?.name ?? 'Agent Store'}</h1>
            <p>{selected?.description || '创建企业智能体，挂载 Skill、MCP Server、CLI 服务，并直接进行调用测试。'}</p>
          </div>
          <div className="agent-store-status">
            {busy && <span className="tag"><span className="spinner" /> 处理中</span>}
            {selected && <span className="tag">{selected.enabled ? 'Enabled' : 'Disabled'}</span>}
          </div>
        </div>

        {error && <div className="alert-error">{error}</div>}
        {notice && <div className="alert-success">{notice}</div>}

        <div className="agent-store-tabs">
          {(['profile', 'skills', 'mcp', 'cli', 'test'] as Tab[]).map(item => (
            <button key={item} className={tab === item ? 'active' : ''} onClick={() => setTab(item)} type="button">
              {tabLabel(item)}
            </button>
          ))}
        </div>

        {tab === 'profile' && (
          <ProfileSection
            form={form}
            setForm={setForm}
            isNew={isNew}
            providers={providers}
            providerModels={providerModels}
            busy={busy}
            onSave={saveAgent}
            onDelete={removeAgent}
          />
        )}
        {tab === 'skills' && selected && (
          <SkillsSection
            agent={selected}
            skills={skills}
            busy={busy}
            onAdd={(skillId, priority) => run(async () => {
              await addAgentStoreSkill(selected.id, { skillId, priority })
              await reload(selected.id)
            }, 'Skill 已挂载')}
            onRemove={(rowId) => run(async () => {
              await removeAgentStoreSkill(selected.id, rowId)
              await reload(selected.id)
            }, 'Skill 已移除')}
          />
        )}
        {tab === 'mcp' && selected && (
          <McpSection
            agent={selected}
            busy={busy}
            onAdd={(req) => run(async () => {
              await addAgentStoreMcpServer(selected.id, req)
              await reload(selected.id)
            }, 'MCP Server 已关联')}
            onRemove={(rowId) => run(async () => {
              await removeAgentStoreMcpServer(selected.id, rowId)
              await reload(selected.id)
            }, 'MCP Server 已移除')}
          />
        )}
        {tab === 'cli' && selected && (
          <CliSection
            agent={selected}
            busy={busy}
            onAdd={(req) => run(async () => {
              await addAgentStoreCliService(selected.id, req)
              await reload(selected.id)
            }, 'CLI 服务已关联')}
            onRemove={(rowId) => run(async () => {
              await removeAgentStoreCliService(selected.id, rowId)
              await reload(selected.id)
            }, 'CLI 服务已移除')}
          />
        )}
        {tab === 'test' && selected && <TestSection agent={selected} />}
        {tab !== 'profile' && !selected && <div className="agent-store-empty panel">请先选择或创建 Agent</div>}
      </main>
    </div>
  )
}

function ProfileSection({
  form, setForm, isNew, providers, providerModels, busy, onSave, onDelete,
}: {
  form: AgentForm
  setForm: (value: AgentForm) => void
  isNew: boolean
  providers: ProviderDto[]
  providerModels: string[]
  busy: boolean
  onSave: () => void
  onDelete: () => void
}) {
  return (
    <section className="agent-store-panel profile-grid">
      <label>
        <span>Agent ID</span>
        <input value={form.id} disabled={!isNew} onChange={e => setForm({ ...form, id: normalId(e.target.value) })} placeholder="research-agent" />
      </label>
      <label>
        <span>名称</span>
        <input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} placeholder="研究分析 Agent" />
      </label>
      <label className="wide">
        <span>描述</span>
        <input value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} placeholder="负责市场分析、资料检索和结构化输出" />
      </label>
      <label>
        <span>Provider</span>
        <select value={form.defaultProviderId} onChange={e => setForm({ ...form, defaultProviderId: e.target.value, defaultModel: '' })}>
          <option value="">默认 Provider</option>
          {providers.map(provider => <option key={provider.providerId} value={provider.providerId}>{provider.providerId}</option>)}
        </select>
      </label>
      <label>
        <span>模型</span>
        <select value={form.defaultModel} onChange={e => setForm({ ...form, defaultModel: e.target.value })}>
          <option value="">默认模型</option>
          {providerModels.map(model => <option key={model} value={model}>{model}</option>)}
        </select>
      </label>
      <label className="wide">
        <span>系统提示词</span>
        <textarea value={form.systemPrompt} onChange={e => setForm({ ...form, systemPrompt: e.target.value })} rows={9} placeholder="定义 Agent 的角色、边界和输出风格" />
      </label>
      <div className="agent-store-actions wide">
        <button className="primary-inline-btn" disabled={busy || !form.id || !form.name} onClick={onSave} type="button">保存 Agent</button>
        {!isNew && <button className="danger-btn" disabled={busy} onClick={onDelete} type="button">删除 Agent</button>}
      </div>
    </section>
  )
}

function SkillsSection({
  agent, skills, busy, onAdd, onRemove,
}: {
  agent: AgentStoreAgentDto
  skills: SkillManifestDto[]
  busy: boolean
  onAdd: (skillId: string, priority: number) => void
  onRemove: (rowId: number) => void
}) {
  const [skillId, setSkillId] = useState('')
  const [priority, setPriority] = useState(0)
  const mounted = new Set(agent.skills.map(skill => skill.skillId))
  const available = skills.filter(skill => !mounted.has(skill.id))

  return (
    <section className="agent-store-panel split-panel">
      <div>
        <h2>已挂载 Skill</h2>
        <div className="agent-store-table">
          {agent.skills.map(row => (
            <div className="agent-store-table-row" key={row.id}>
              <div><strong>{row.skillId}</strong><small>Priority {row.priority}</small></div>
              <span className="mini-status">{row.enabled ? 'Enabled' : 'Disabled'}</span>
              <button className="danger-btn" disabled={busy} onClick={() => onRemove(row.id)} type="button">移除</button>
            </div>
          ))}
          {agent.skills.length === 0 && <div className="agent-store-empty">还没有挂载 Skill</div>}
        </div>
      </div>
      <div className="agent-store-side-form">
        <h2>挂载 Skill</h2>
        <label><span>Skill</span><select value={skillId} onChange={e => setSkillId(e.target.value)}>
          <option value="">选择 Skill</option>
          {available.map(skill => <option key={skill.id} value={skill.id}>{skill.name} · {skill.id}</option>)}
        </select></label>
        <label><span>优先级</span><input type="number" value={priority} onChange={e => setPriority(Number(e.target.value))} /></label>
        <button className="primary-inline-btn" disabled={busy || !skillId} onClick={() => onAdd(skillId, priority)} type="button">挂载</button>
      </div>
    </section>
  )
}

function McpSection({ agent, busy, onAdd, onRemove }: {
  agent: AgentStoreAgentDto
  busy: boolean
  onAdd: (req: { mcpServerId: string; serverName: string; command: string; arguments?: string[] }) => void
  onRemove: (rowId: number) => void
}) {
  const [serverId, setServerId] = useState('')
  const [serverName, setServerName] = useState('')
  const [command, setCommand] = useState('')
  const [args, setArgs] = useState('')

  return (
    <section className="agent-store-panel split-panel">
      <div>
        <h2>MCP Server</h2>
        <div className="agent-store-table">
          {agent.mcpServers.map(row => (
            <div className="agent-store-table-row" key={row.id}>
              <div><strong>{row.serverName}</strong><small>{row.mcpServerId}</small></div>
              <span className="mini-status">{row.enabled ? 'Enabled' : 'Disabled'}</span>
              <button className="danger-btn" disabled={busy} onClick={() => onRemove(row.id)} type="button">移除</button>
            </div>
          ))}
          {agent.mcpServers.length === 0 && <div className="agent-store-empty">还没有关联 MCP Server</div>}
        </div>
      </div>
      <div className="agent-store-side-form">
        <h2>新增关联</h2>
        <label><span>Server ID</span><input value={serverId} onChange={e => setServerId(e.target.value)} placeholder="filesystem" /></label>
        <label><span>名称</span><input value={serverName} onChange={e => setServerName(e.target.value)} placeholder="File System" /></label>
        <label><span>启动命令</span><input value={command} onChange={e => setCommand(e.target.value)} placeholder="npx" /></label>
        <label><span>参数</span><input value={args} onChange={e => setArgs(e.target.value)} placeholder="-y @modelcontextprotocol/server-filesystem ./" /></label>
        <button className="primary-inline-btn" disabled={busy || !serverId || !serverName || !command} onClick={() => onAdd({ mcpServerId: serverId, serverName, command, arguments: splitArgs(args) })} type="button">关联 MCP</button>
      </div>
    </section>
  )
}

function CliSection({ agent, busy, onAdd, onRemove }: {
  agent: AgentStoreAgentDto
  busy: boolean
  onAdd: (req: { cliServiceId: string; serviceName: string; baseUri: string; authConfig?: unknown }) => void
  onRemove: (rowId: number) => void
}) {
  const [serviceId, setServiceId] = useState('')
  const [serviceName, setServiceName] = useState('')
  const [baseUri, setBaseUri] = useState('')
  const [authConfig, setAuthConfig] = useState('{\n  "type": "apiKey",\n  "header": "Authorization",\n  "value": "Bearer ..."\n}')
  const [authError, setAuthError] = useState<string | null>(null)

  function submit() {
    setAuthError(null)
    let parsed: unknown = undefined
    if (authConfig.trim()) {
      try { parsed = JSON.parse(authConfig) } catch { setAuthError('认证配置不是有效 JSON'); return }
    }
    onAdd({ cliServiceId: serviceId, serviceName, baseUri, authConfig: parsed })
  }

  return (
    <section className="agent-store-panel split-panel">
      <div>
        <h2>CLI 服务</h2>
        <div className="agent-store-table">
          {agent.cliServices.map(row => (
            <div className="agent-store-table-row" key={row.id}>
              <div><strong>{row.serviceName}</strong><small>{row.baseUri || row.cliServiceId}</small></div>
              <span className="mini-status">{row.enabled ? 'Enabled' : 'Disabled'}</span>
              <button className="danger-btn" disabled={busy} onClick={() => onRemove(row.id)} type="button">移除</button>
            </div>
          ))}
          {agent.cliServices.length === 0 && <div className="agent-store-empty">还没有关联 CLI 服务</div>}
        </div>
      </div>
      <div className="agent-store-side-form">
        <h2>新增关联</h2>
        {authError && <div className="alert-error compact">{authError}</div>}
        <label><span>Service ID</span><input value={serviceId} onChange={e => setServiceId(e.target.value)} placeholder="github-cli" /></label>
        <label><span>名称</span><input value={serviceName} onChange={e => setServiceName(e.target.value)} placeholder="GitHub CLI" /></label>
        <label><span>Base URI</span><input value={baseUri} onChange={e => setBaseUri(e.target.value)} placeholder="https://api.github.com" /></label>
        <label><span>认证配置 JSON</span><textarea rows={7} value={authConfig} onChange={e => setAuthConfig(e.target.value)} /></label>
        <button className="primary-inline-btn" disabled={busy || !serviceId || !serviceName} onClick={submit} type="button">关联 CLI</button>
      </div>
    </section>
  )
}

function TestSection({ agent }: { agent: AgentStoreAgentDto }) {
  const [tools, setTools] = useState<AgentStoreToolDto[]>([])
  const [toolName, setToolName] = useState('')
  const [args, setArgs] = useState('{}')
  const [toolResult, setToolResult] = useState('')
  const [message, setMessage] = useState('')
  const [reply, setReply] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    listAgentStoreTools(agent.id)
      .then(rows => { setTools(rows); setToolName(rows[0]?.name ?? '') })
      .catch(err => setError(String(err.message ?? err)))
  }, [agent.id])

  async function runTool() {
    setBusy(true)
    setError(null)
    setToolResult('')
    try {
      setToolResult(await testAgentStoreTool(agent.id, toolName, args))
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  async function runChat() {
    setBusy(true)
    setError(null)
    setReply('')
    try {
      const chunks = await interactAgentStore(agent.id, message)
      setReply(chunks.map(chunk => chunk.content).join(''))
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  const selectedTool = tools.find(tool => tool.name === toolName)

  return (
    <section className="agent-store-panel test-grid">
      {error && <div className="alert-error wide">{error}</div>}
      <div className="agent-store-side-form">
        <h2>工具调用测试</h2>
        <label><span>工具</span><select value={toolName} onChange={e => setToolName(e.target.value)}>
          {tools.length === 0 && <option value="">没有可用工具</option>}
          {tools.map(tool => <option key={`${tool.skillId}:${tool.name}`} value={tool.name}>{tool.name}</option>)}
        </select></label>
        {selectedTool?.description && <p className="muted-small">{selectedTool.description}</p>}
        <label><span>参数 JSON</span><textarea rows={8} value={args} onChange={e => setArgs(e.target.value)} /></label>
        <button className="primary-inline-btn" disabled={busy || !toolName} onClick={runTool} type="button">调用工具</button>
        <pre className="agent-store-output">{toolResult}</pre>
      </div>
      <div className="agent-store-side-form">
        <h2>Agent 对话测试</h2>
        <label><span>消息</span><textarea rows={10} value={message} onChange={e => setMessage(e.target.value)} placeholder="向这个 Agent 发送一条测试消息" /></label>
        <button className="primary-inline-btn" disabled={busy || !message.trim()} onClick={runChat} type="button">发送测试</button>
        <pre className="agent-store-output">{reply}</pre>
      </div>
    </section>
  )
}

function fromAgent(agent: AgentStoreAgentDto): AgentForm {
  return {
    id: agent.id,
    name: agent.name,
    description: agent.description ?? '',
    defaultProviderId: agent.defaultProviderId ?? '',
    defaultModel: agent.defaultModel ?? '',
    systemPrompt: agent.systemPrompt ?? '',
  }
}

function normalId(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9-_]/g, '-')
}

function splitArgs(value: string) {
  return value.split(/\s+/).map(part => part.trim()).filter(Boolean)
}

function tabLabel(tab: Tab) {
  switch (tab) {
    case 'profile': return '资料'
    case 'skills': return 'Skill'
    case 'mcp': return 'MCP'
    case 'cli': return 'CLI'
    case 'test': return '测试'
  }
}
