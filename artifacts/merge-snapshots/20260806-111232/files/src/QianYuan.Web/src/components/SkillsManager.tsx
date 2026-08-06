import { useEffect, useMemo, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import type {
  CreateSkillRequest,
  InstalledSkillDto,
  McpStdioRegistrationRequest,
  SkillCategoryDto,
  SkillManifestDto,
  SkillMarketEntryDto,
  SkillPackageDto,
  SkillToolsResponse,
} from '../types/api'
import {
  createSkill,
  installSkill,
  listInstalledSkills,
  listSkillCategories,
  listSkillMarket,
  listSkills,
  listSkillTools,
  registerMcpStdio,
  relevantSkills,
  setSkillEnabled,
  uninstallSkill,
} from '../services/api'

type Tab = 'market' | 'catalog' | 'installed' | 'create' | 'relevant' | 'register'

interface Props { onClose: () => void }

export function SkillsManager({ onClose }: Props) {
  const [tab, setTab] = useState<Tab>('market')
  const [skills, setSkills] = useState<SkillManifestDto[]>([])
  const [packages, setPackages] = useState<SkillPackageDto[]>([])
  const [installed, setInstalled] = useState<InstalledSkillDto[]>([])
  const [categories, setCategories] = useState<SkillCategoryDto[]>([])

  useEffect(() => { void reloadAll() }, [])

  async function reloadAll() {
    const [nextSkills, nextPackages, nextInstalled, nextCategories] = await Promise.all([
      listSkills().catch(() => []),
      listSkillMarket().catch(() => []),
      listInstalledSkills().catch(() => []),
      listSkillCategories().catch(() => []),
    ])
    setSkills(nextSkills)
    setPackages(nextPackages)
    setInstalled(nextInstalled)
    setCategories(nextCategories)
  }

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <strong>技能市场</strong>
          <span style={{ flex: 1 }} />
          <button className="ghost" onClick={onClose}>关闭</button>
        </div>
        <div className="modal-tabs">
          <button className={tab === 'market' ? 'active' : ''} onClick={() => setTab('market')}>市场</button>
          <button className={tab === 'catalog' ? 'active' : ''} onClick={() => setTab('catalog')}>技能目录</button>
          <button className={tab === 'installed' ? 'active' : ''} onClick={() => setTab('installed')}>已安装</button>
          <button className={tab === 'create' ? 'active' : ''} onClick={() => setTab('create')}>创建技能</button>
          <button className={tab === 'relevant' ? 'active' : ''} onClick={() => setTab('relevant')}>选择测试</button>
          <button className={tab === 'register' ? 'active' : ''} onClick={() => setTab('register')}>MCP</button>
        </div>
        <div className="modal-body">
          {tab === 'market' && <MarketTab packages={packages} categories={categories} onChanged={reloadAll} />}
          {tab === 'catalog' && <CatalogTab skills={skills} categories={categories} onChanged={reloadAll} />}
          {tab === 'installed' && <InstalledTab installed={installed} onChanged={reloadAll} />}
          {tab === 'create' && <CreateTab categories={categories} onCreated={reloadAll} />}
          {tab === 'relevant' && <RelevantTab />}
          {tab === 'register' && <RegisterMcpTab onRegistered={reloadAll} />}
        </div>
      </div>
    </div>
  )
}

function MarketTab({ packages, categories, onChanged }: { packages: SkillPackageDto[]; categories: SkillCategoryDto[]; onChanged: () => void }) {
  const [category, setCategory] = useState('')
  const [query, setQuery] = useState('')
  const [items, setItems] = useState<SkillPackageDto[] | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const visible = items ?? packages

  async function search() {
    setItems(await listSkillMarket({ category: category || undefined, q: query || undefined }))
  }

  async function install(entry: SkillMarketEntryDto) {
    setBusy(entry.id)
    try {
      await installSkill({ marketEntryId: entry.id, enabled: true })
      await onChanged()
      await search()
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="skill-market-panel">
      <div className="skill-filter-row">
        <select value={category} onChange={e => setCategory(e.target.value)}>
          <option value="">全部分类</option>
          {categories.map(c => <option key={c.id} value={c.id}>{c.name} ({c.marketCount})</option>)}
        </select>
        <input value={query} onChange={e => setQuery(e.target.value)} placeholder="搜索名称、标签、触发短语..." />
        <button onClick={search}>搜索</button>
      </div>
      {visible.length === 0 && <div className="small">未找到市场技能。</div>}
      {visible.map(pkg => <section key={pkg.id} className="skill-package-card">
        <div className="skill-package-head">
          <div><strong>{pkg.name}</strong><div className="small">{pkg.description}</div></div>
          <span className="tag">{pkg.category}</span>
        </div>
        {pkg.entries.map(entry => <SkillMarketCard key={entry.id} entry={entry} busy={busy === entry.id} onInstall={() => install(entry)} />)}
      </section>)}
    </div>
  )
}

function SkillMarketCard({ entry, busy, onInstall }: { entry: SkillMarketEntryDto; busy: boolean; onInstall: () => void }) {
  return <div className="skill-card">
    <div className="skill-card-head">
      <div style={{ flex: 1 }}>
        <strong>{entry.name}</strong>
        <span className="tag" style={{ marginLeft: 8 }}>{entry.category}</span>
        {entry.installed && <span className="tag" style={{ marginLeft: 4 }}>已安装</span>}
        <div className="small" style={{ marginTop: 2 }}>{entry.description}</div>
        <TagLine label="标签" values={entry.tags} />
        <TagLine label="触发短语" values={entry.triggerPhrases} />
      </div>
      <button onClick={onInstall} disabled={busy || entry.installed}>{entry.installed ? '已安装' : busy ? '安装中...' : '安装'}</button>
    </div>
  </div>
}

function CatalogTab({ skills, categories, onChanged }: { skills: SkillManifestDto[]; categories: SkillCategoryDto[]; onChanged: () => void }) {
  const [tools, setTools] = useState<Record<string, SkillToolsResponse | null>>({})
  const [expanded, setExpanded] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [category, setCategory] = useState('')
  const filtered = useMemo(() => category ? skills.filter(s => s.category === category) : skills, [skills, category])

  async function expand(id: string) {
    if (expanded === id) { setExpanded(null); return }
    setExpanded(id)
    if (tools[id] === undefined) setTools(prev => ({ ...prev, [id]: undefined as unknown as SkillToolsResponse | null }))
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

  return <div>
    <div className="skill-filter-row">
      <select value={category} onChange={e => setCategory(e.target.value)}>
        <option value="">全部分类</option>
        {categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
      </select>
    </div>
    {filtered.length === 0 && <div className="small">No skills registered.</div>}
    {filtered.map(s => {
      const t = tools[s.id]
      return <div key={s.id} className="skill-card">
        <div className="skill-card-head">
          <div style={{ flex: 1, cursor: 'pointer' }} onClick={() => expand(s.id)}>
            <strong>{s.name}</strong>
            <span className="tag" style={{ marginLeft: 8 }}>{s.id}</span>
            {s.category && <span className="tag" style={{ marginLeft: 4 }}>{s.category}</span>}
            {s.requiresNetwork && <span className="tag" style={{ marginLeft: 4 }}>Network</span>}
            {s.requiresFilesystem && <span className="tag" style={{ marginLeft: 4 }}>Filesystem</span>}
            <div className="small" style={{ marginTop: 2 }}>{s.description}</div>
            <TagLine label="触发短语" values={s.triggerPhrases ?? []} />
          </div>
          <label className="switch" title={s.enabled ? '停用' : '启用'}>
            <input type="checkbox" checked={s.enabled !== false} disabled={busy === s.id} onChange={e => toggle(s.id, e.target.checked)} />
            <span>{s.enabled !== false ? '已启用' : '已停用'}</span>
          </label>
        </div>
        {expanded === s.id && <div className="skill-tools">
          {t === null && <div className="small">工具加载失败。</div>}
          {t === undefined && <div className="small">加载中...</div>}
          {t && <>
            {t.systemPromptFragment && <details><summary className="small">System prompt fragment</summary><pre>{t.systemPromptFragment}</pre></details>}
            {t.tools.length === 0 && <div className="small">Prompt-only skill; no tools.</div>}
            {t.tools.map(tool => <div key={tool.name} className="tool-row"><strong>{tool.name}</strong><span>{tool.description}</span></div>)}
          </>}
        </div>}
      </div>
    })}
  </div>
}

function InstalledTab({ installed, onChanged }: { installed: InstalledSkillDto[]; onChanged: () => void }) {
  const [busy, setBusy] = useState<string | null>(null)
  async function toggle(skillId: string, enabled: boolean) {
    setBusy(skillId)
    try { await setSkillEnabled(skillId, enabled); await onChanged() } finally { setBusy(null) }
  }
  async function remove(skillId: string) {
    if (!confirm(`确认卸载 ${skillId}？`)) return
    setBusy(skillId)
    try { await uninstallSkill(skillId); await onChanged() } finally { setBusy(null) }
  }
  return <div>
    {installed.length === 0 && <div className="small">尚未安装市场技能或自定义技能。</div>}
    {installed.map(s => <div key={s.skillId} className="skill-card">
      <div className="skill-card-head">
        <div style={{ flex: 1 }}>
          <strong>{s.name}</strong><span className="tag" style={{ marginLeft: 8 }}>{s.skillId}</span><span className="tag" style={{ marginLeft: 4 }}>{s.scope}</span>
          <div className="small">{s.description}</div>
          <div className="small">Path: {s.installPath || '(registry only)'}</div>
        </div>
        <button disabled={busy === s.skillId} onClick={() => toggle(s.skillId, !s.enabled)}>{s.enabled ? '停用' : '启用'}</button>
        <button disabled={busy === s.skillId} onClick={() => remove(s.skillId)}>卸载</button>
      </div>
    </div>)}
  </div>
}

function CreateTab({ categories, onCreated }: { categories: SkillCategoryDto[]; onCreated: () => void }) {
  const [form, setForm] = useState<CreateSkillRequest>({ id: '', name: '', description: '', body: '', category: 'general', tags: [], triggerPhrases: [], scope: 'user' })
  const [tags, setTags] = useState('')
  const [triggers, setTriggers] = useState('')
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true); setResult(null)
    try {
      const created = await createSkill({ ...form, tags: splitList(tags), triggerPhrases: splitList(triggers) })
      setResult(`已创建 ${created.skillId}`)
      setForm({ id: '', name: '', description: '', body: '', category: 'general', tags: [], triggerPhrases: [], scope: 'user' })
      setTags(''); setTriggers('')
      onCreated()
    } catch (err) {
      setResult(err instanceof Error ? err.message : '创建失败')
    } finally { setBusy(false) }
  }

  return <form onSubmit={submit} className="skill-create-form">
    <Field label="技能 ID"><input value={form.id} onChange={e => setForm({ ...form, id: e.target.value })} placeholder="可选，例如 report-writer" /></Field>
    <Field label="名称"><input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></Field>
    <Field label="描述"><input value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} required /></Field>
    <Field label="分类"><select value={form.category ?? 'general'} onChange={e => setForm({ ...form, category: e.target.value })}><option value="general">通用</option>{categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></Field>
    <Field label="标签（逗号分隔）"><input value={tags} onChange={e => setTags(e.target.value)} placeholder="写作, 报告" /></Field>
    <Field label="触发短语（逗号分隔）"><input value={triggers} onChange={e => setTriggers(e.target.value)} placeholder="写报告, 总结文件" /></Field>
    <Field label="技能正文"><textarea rows={8} value={form.body} onChange={e => setForm({ ...form, body: e.target.value })} required placeholder="描述该技能应该在何时、如何被智能体使用。" /></Field>
    <button disabled={busy || !form.name.trim() || !form.body.trim()}>{busy ? '创建中...' : '创建技能'}</button>
    {result && <div className="small">{result}</div>}
  </form>
}

function RelevantTab() {
  const [q, setQ] = useState('')
  const [results, setResults] = useState<SkillManifestDto[] | null>(null)
  async function run() { setResults(await relevantSkills(q, 8)) }
  return <div>
    <Field label="用户意图"><textarea value={q} onChange={e => setQ(e.target.value)} rows={4} placeholder="描述用户任务，用于测试触发短语/标签加权选择。" /></Field>
    <button onClick={run} disabled={!q.trim()}>选择相关技能</button>
    {results && <div style={{ marginTop: 10 }}>{results.map(s => <div key={s.id} className="skill-card"><strong>{s.name}</strong><span className="tag" style={{ marginLeft: 8 }}>{s.category}</span><div className="small">{s.description}</div><TagLine label="触发短语" values={s.triggerPhrases ?? []} /></div>)}</div>}
  </div>
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
        const value = line.trim()
        const idx = value.indexOf('=')
        if (idx > 0) env[value.slice(0, idx)] = value.slice(idx + 1)
      }
      const req: McpStdioRegistrationRequest = { serverId: serverId.trim(), command: command.trim(), arguments: splitLines(argsText), environment: env }
      const r = await registerMcpStdio(req)
      if (r.ok) { setResult(`已注册 ${r.skillId}`); setServerId(''); setCommand(''); setArgsText(''); setEnvText(''); onRegistered() }
      else setResult(`注册失败：${r.error}`)
    } finally { setBusy(false) }
  }

  return <div>
    <div className="small" style={{ marginBottom: 8 }}>将外部 MCP 服务注册为当前进程可用的运行时技能。</div>
    <Field label="服务 ID"><input value={serverId} onChange={e => setServerId(e.target.value)} placeholder="filesystem" /></Field>
    <Field label="启动命令"><input value={command} onChange={e => setCommand(e.target.value)} placeholder="npx" /></Field>
    <Field label="参数"><textarea value={argsText} onChange={e => setArgsText(e.target.value)} rows={4} placeholder={'-y\n@modelcontextprotocol/server-filesystem\n/tmp'} /></Field>
    <Field label="环境变量"><textarea value={envText} onChange={e => setEnvText(e.target.value)} rows={3} placeholder="KEY=VALUE" /></Field>
    <button onClick={submit} disabled={busy || !serverId.trim() || !command.trim()}>{busy ? '注册中...' : '注册 MCP'}</button>
    {result && <div className="small">{result}</div>}
  </div>
}

function TagLine({ label, values }: { label: string; values: string[] }) {
  if (!values.length) return null
  return <div className="small" style={{ marginTop: 4 }}>{label}: {values.slice(0, 8).map(v => <span className="tag" key={v} style={{ marginLeft: 4 }}>{v}</span>)}</div>
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return <label style={{ display: 'grid', gap: 4, marginBottom: 8 }}><span className="small">{label}</span>{children}</label>
}

function splitList(value: string) {
  return value.split(/[,;\n]/).map(v => v.trim()).filter(Boolean)
}

function splitLines(value: string) {
  return value.split('\n').map(v => v.trim()).filter(Boolean)
}
