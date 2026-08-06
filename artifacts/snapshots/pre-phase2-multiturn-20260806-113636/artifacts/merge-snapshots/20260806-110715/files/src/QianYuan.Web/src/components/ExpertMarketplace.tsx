import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import type {
  AgentStoreAgentDto,
  CustomExpertUpsertRequest,
  ExpertCategoryDto,
  ExpertDetailDto,
  ExpertScenarioDto,
  ExpertSummaryDto,
  CreateSkillRequest,
  InstalledSkillDto,
  SkillCategoryDto,
  SkillMarketEntryDto,
  SkillPackageDto,
} from '../types/api'
import {
  createCustomExpert,
  createSkill,
  deleteCustomExpert,
  getExpert,
  installSkill,
  listAgentStoreAgents,
  listExpertCategories,
  listExpertScenarios,
  listExperts,
  listInstalledSkills,
  listSkillCategories,
  listSkillMarket,
  updateCustomExpert,
} from '../services/api'

type TopTab = 'experts' | 'skills' | 'connectors'
type ExpertKind = 'agent' | 'team'
type SortMode = 'hot' | 'newest'

type ExpertForm = {
  id: string
  name: string
  profession: string
  description: string
  systemPrompt: string
  categoryId: string
  avatarUrl: string
  tags: string
  quickPrompts: string
  boundAgentId: string
  author: string
}

interface Props {
  onBack: () => void
  onLaunch: (prompt: string, expert: ExpertDetailDto) => void
}

const emptyForm: ExpertForm = {
  id: '',
  name: '',
  profession: '',
  description: '',
  systemPrompt: '',
  categoryId: 'custom',
  avatarUrl: '',
  tags: '',
  quickPrompts: '',
  boundAgentId: '',
  author: 'QIANYUAN User',
}

function initials(name: string): string {
  const trimmed = name.trim()
  return trimmed ? trimmed.slice(0, 1).toUpperCase() : '?'
}

function Avatar({ expert, size = 40 }: { expert: ExpertSummaryDto; size?: number }) {
  const [failed, setFailed] = useState(false)
  const style = { width: size, height: size, fontSize: size * 0.4 }
  if (failed || !expert.avatarUrl) return <span className="expert-avatar fallback" style={style}>{initials(expert.name)}</span>
  return <img className="expert-avatar" style={style} src={expert.avatarUrl} alt={expert.name} loading="lazy" onError={() => setFailed(true)} />
}

export function ExpertMarketplace({ onBack, onLaunch }: Props) {
  const [topTab, setTopTab] = useState<TopTab>('experts')
  const [kind, setKind] = useState<ExpertKind>('agent')
  const [sort, setSort] = useState<SortMode>('hot')
  const [category, setCategory] = useState<string>('all')
  const [query, setQuery] = useState('')
  const [customOnly, setCustomOnly] = useState(false)
  const [refreshNonce, setRefreshNonce] = useState(0)
  const [skillInstalledOnly, setSkillInstalledOnly] = useState(false)
  const [showSkillCreate, setShowSkillCreate] = useState(false)
  const [skillInstalledCount, setSkillInstalledCount] = useState(0)

  const [categories, setCategories] = useState<ExpertCategoryDto[]>([])
  const [scenarios, setScenarios] = useState<ExpertScenarioDto[]>([])
  const [experts, setExperts] = useState<ExpertSummaryDto[]>([])
  const [agents, setAgents] = useState<AgentStoreAgentDto[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [detail, setDetail] = useState<ExpertDetailDto | null>(null)
  const [detailBusy, setDetailBusy] = useState(false)
  const [showForm, setShowForm] = useState(false)
  const [form, setForm] = useState<ExpertForm>(emptyForm)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    Promise.all([listExpertCategories(), listExpertScenarios(), listAgentStoreAgents().catch(() => [])])
      .then(([cats, scens, agentRows]) => { setCategories(cats); setScenarios(scens); setAgents(agentRows) })
      .catch(err => setError(err instanceof Error ? err.message : String(err)))
  }, [])

  useEffect(() => {
    let alive = true
    setLoading(true)
    setError(null)
    listExperts({ category, type: kind, sort, q: query, isCustom: customOnly ? true : undefined })
      .then(res => {
        if (!alive) return
        setExperts(res.items)
        setTotal(res.total)
      })
      .catch(err => { if (alive) setError(err instanceof Error ? err.message : String(err)) })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [category, kind, sort, query, customOnly, refreshNonce])

  const categoryChips = useMemo(() => {
    const customCount = experts.filter(e => e.isCustom).length
    const chips = [{ id: 'all', name: '全部', description: '', count: total }, ...categories]
    if (customCount > 0 || customOnly) chips.splice(1, 0, { id: 'custom', name: '自定义', description: '我的专家', count: customCount })
    return chips
  }, [categories, experts, total, customOnly])

  async function openDetail(id: string) {
    setDetailBusy(true)
    try {
      setDetail(await getExpert(id))
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setDetailBusy(false)
    }
  }

  function launch(prompt: string) {
    if (!detail) return
    onLaunch(prompt, detail)
    setDetail(null)
  }

  function startCreate() {
    setEditingId(null)
    setForm(emptyForm)
    setShowForm(true)
  }

  function startEdit(expert: ExpertDetailDto) {
    setEditingId(expert.id)
    setForm({
      id: expert.id,
      name: expert.name,
      profession: expert.profession,
      description: expert.description,
      systemPrompt: '',
      categoryId: expert.categoryId || 'custom',
      avatarUrl: expert.avatarUrl || '',
      tags: expert.tags.join(', '),
      quickPrompts: expert.quickPrompts.join('\n'),
      boundAgentId: expert.boundAgentId || '',
      author: expert.author || 'QIANYUAN User',
    })
    setShowForm(true)
  }

  async function submitForm() {
    setSaving(true)
    setError(null)
    try {
      const payload: CustomExpertUpsertRequest = {
        id: editingId ? undefined : form.id || undefined,
        name: form.name,
        profession: form.profession,
        description: form.description,
        systemPrompt: form.systemPrompt || `你是${form.name}，一位${form.profession}。${form.description}`,
        categoryId: form.categoryId || 'custom',
        avatarUrl: form.avatarUrl || undefined,
        tags: splitValues(form.tags),
        quickPrompts: splitLines(form.quickPrompts),
        boundAgentId: form.boundAgentId || undefined,
        author: form.author || undefined,
      }
      const saved = editingId ? await updateCustomExpert(editingId, payload) : await createCustomExpert(payload)
      setShowForm(false)
      setEditingId(null)
      setForm(emptyForm)
      setRefreshNonce(n => n + 1)
      setDetail(saved)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }

  async function removeCustomExpert(id: string) {
    if (!confirm('确定删除这个自定义专家吗？')) return
    setSaving(true)
    try {
      await deleteCustomExpert(id)
      setDetail(null)
      setRefreshNonce(n => n + 1)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="expert-market">
      <header className="market-topbar">
        <div className="market-tabs">
          <button className="market-back" type="button" onClick={onBack} title="返回">←</button>
          <button className={`market-tab ${topTab === 'experts' ? 'active' : ''}`} type="button" onClick={() => setTopTab('experts')}>
            <span className="mt-ico">★</span>专家
          </button>
          <button className={`market-tab ${topTab === 'skills' ? 'active' : ''}`} type="button" onClick={() => setTopTab('skills')}>
            <span className="mt-ico">◆</span>技能
          </button>
          <button className={`market-tab ${topTab === 'connectors' ? 'active' : ''}`} type="button" onClick={() => setTopTab('connectors')}>
            <span className="mt-ico">⚡</span>连接器
          </button>
        </div>
        <div className="market-actions">
          <label className="market-search"><span>⌕</span><input value={query} onChange={e => setQuery(e.target.value)} placeholder={topTab === 'skills' ? '搜索技能' : topTab === 'connectors' ? '搜索连接器' : '搜索专家或专家团'} /></label>
          {topTab === 'skills' ? (
            <>
              <button className={`market-mine ${skillInstalledOnly ? 'active' : ''}`} type="button" onClick={() => setSkillInstalledOnly(v => !v)}>我安装的 {skillInstalledCount}</button>
              <button className="market-mine primary" type="button" onClick={() => setShowSkillCreate(true)}>添加技能</button>
            </>
          ) : topTab === 'experts' ? (
            <>
              <button className={`market-mine ${customOnly ? 'active' : ''}`} type="button" onClick={() => setCustomOnly(v => !v)}>我的专家</button>
              <button className="market-mine primary" type="button" onClick={startCreate}>新建专家</button>
            </>
          ) : (
            <button className="market-mine" type="button" disabled>即将上线</button>
          )}
        </div>
      </header>

      {topTab === 'skills' ? (
        <SkillMarketplaceView query={query} installedOnly={skillInstalledOnly} createOpen={showSkillCreate} onCreateClose={() => setShowSkillCreate(false)} onInstalledCountChange={setSkillInstalledCount} />
      ) : topTab === 'connectors' ? (
        <div className="market-body"><div className="market-empty-tab">连接器市场即将上线。</div></div>
      ) : (
        <div className="market-body">
          {!customOnly && scenarios.length > 0 && (
            <section className="market-featured">
              <h3>精选场景</h3>
              <div className="scenario-scroller">
                {scenarios.map(sc => (
                  <div key={sc.id} className="scenario-card" style={{ ['--accent' as string]: sc.accent }}>
                    <div className="scenario-head"><strong>{sc.name}</strong></div>
                    <div className="scenario-experts">
                      {sc.experts.map(e => (
                        <button key={e.id} className="scenario-expert" type="button" onClick={() => openDetail(e.id)} title={e.profession}>
                          <Avatar expert={e} size={26} /><span>{e.profession}</span>
                        </button>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </section>
          )}

          <section className="market-list">
            <div className="market-list-head">
              <div className="kind-toggle">
                <button className={kind === 'agent' ? 'active' : ''} type="button" onClick={() => setKind('agent')}>专家</button>
                <button className={kind === 'team' ? 'active' : ''} type="button" onClick={() => setKind('team')}>专家团</button>
              </div>
              <div className="sort-toggle">
                <button className={sort === 'hot' ? 'active' : ''} type="button" onClick={() => setSort('hot')}>热门</button>
                <button className={sort === 'newest' ? 'active' : ''} type="button" onClick={() => setSort('newest')}>最新</button>
              </div>
            </div>

            <div className="category-chips">
              {categoryChips.map(c => (
                <button key={c.id} className={`chip ${category === c.id ? 'active' : ''}`} type="button" onClick={() => setCategory(c.id)}>{c.name}</button>
              ))}
            </div>

            {error && <div className="market-error">{error}</div>}
            {loading && experts.length === 0 && <div className="market-loading">加载中...</div>}
            {!loading && experts.length === 0 && !error && <div className="market-loading">未找到匹配的专家</div>}

            <div className="expert-grid">
              {experts.map(e => (
                <button key={e.id} className="expert-card" type="button" onClick={() => openDetail(e.id)}>
                  <div className="ec-top">
                    <Avatar expert={e} size={40} />
                    <div className="ec-head">
                      <div className="ec-name">
                        <strong>{e.name}</strong>
                        {e.type === 'team' && <em className="ec-badge team">专家团</em>}
                        {e.isOpc && <em className="ec-badge opc">OPC</em>}
                        {e.isCustom && <em className="ec-badge custom">自定义</em>}
                      </div>
                      <span className="ec-profession">{e.profession}</span>
                    </div>
                  </div>
                  <p className="ec-desc">{e.description}</p>
                  <div className="ec-tags">{e.tags.slice(0, 3).map(t => <span key={t}>{t}</span>)}</div>
                </button>
              ))}
            </div>
          </section>
        </div>
      )}

      {(detail || detailBusy) && (
        <div className="modal-backdrop expert-detail-backdrop" onClick={() => setDetail(null)}>
          <div className="expert-detail-card" onClick={e => e.stopPropagation()}>
            {detailBusy && !detail ? <div className="market-loading">加载中...</div> : detail && (
              <>
                <div className="ed-banner" style={{ ['--accent' as string]: bannerAccent(detail.categoryId) }}>
                  <button className="ed-close" type="button" onClick={() => setDetail(null)} aria-label="关闭">×</button>
                  <div className="ed-hero">
                    <Avatar expert={detail} size={64} />
                    <div className="ed-hero-text">
                      <div className="ed-name">
                        <strong>{detail.name}</strong>
                        {detail.type === 'team' && <em className="ec-badge team">专家团</em>}
                        {detail.isOpc && <em className="ec-badge opc">OPC</em>}
                        {detail.isCustom && <em className="ec-badge custom">自定义</em>}
                      </div>
                      <span className="ed-profession">{detail.profession}</span>
                      <div className="ed-meta">
                        <span className="ed-meta-item">{detail.categoryName}</span>
                        {detail.boundAgentId && <span className="ed-meta-item">Agent: {detail.boundAgentId}</span>}
                        {detail.author && <span className="ed-meta-item">by {detail.author}</span>}
                      </div>
                    </div>
                  </div>
                </div>

                <div className="ed-scroll">
                  <p className="ed-desc">{detail.description}</p>
                  {detail.tags.length > 0 && <div className="ec-tags ed-tags">{detail.tags.map(t => <span key={t}>{t}</span>)}</div>}
                  <div className="ed-prompts">
                    <h4>快速开始</h4>
                    {detail.quickPrompts.length === 0 && detail.defaultInitPrompt && <PromptButton index={1} text={detail.defaultInitPrompt} onClick={() => launch(detail.defaultInitPrompt)} />}
                    {detail.quickPrompts.map((p, i) => <PromptButton key={i} index={i + 1} text={p} onClick={() => launch(p)} />)}
                  </div>
                </div>

                <div className="ed-footer">
                  <span className="ed-footer-hint">召唤后可在输入框继续编辑再发送</span>
                  <div className="ed-actions">
                    {detail.isCustom && <button className="ed-secondary" type="button" disabled={saving} onClick={() => startEdit(detail)}>编辑</button>}
                    {detail.isCustom && <button className="ed-danger" type="button" disabled={saving} onClick={() => removeCustomExpert(detail.id)}>删除</button>}
                    <button className="ed-summon" type="button" onClick={() => launch(detail.defaultInitPrompt || `你好，我需要${detail.profession}的帮助。`)}>召唤专家</button>
                  </div>
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {showForm && (
        <div className="modal-backdrop expert-detail-backdrop" onClick={() => setShowForm(false)}>
          <div className="expert-detail-card custom-expert-form" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <strong>{editingId ? '编辑自定义专家' : '新建自定义专家'}</strong>
              <button className="ghost" type="button" onClick={() => setShowForm(false)}>关闭</button>
            </div>
            <div className="custom-form-grid">
              {!editingId && <label><span>ID（可选）</span><input value={form.id} onChange={e => setForm({ ...form, id: e.target.value })} placeholder="custom-growth-advisor" /></label>}
              <label><span>名称</span><input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} placeholder="增长策略专家" /></label>
              <label><span>职业/定位</span><input value={form.profession} onChange={e => setForm({ ...form, profession: e.target.value })} placeholder="增长策略顾问" /></label>
              <label><span>分类</span><select value={form.categoryId} onChange={e => setForm({ ...form, categoryId: e.target.value })}>
                <option value="custom">自定义</option>
                {categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select></label>
              <label><span>绑定 Agent</span><select value={form.boundAgentId} onChange={e => setForm({ ...form, boundAgentId: e.target.value })}>
                <option value="">不绑定</option>
                {agents.map(a => <option key={a.id} value={a.id}>{a.name || a.id}</option>)}
              </select></label>
              <label><span>头像 URL</span><input value={form.avatarUrl} onChange={e => setForm({ ...form, avatarUrl: e.target.value })} placeholder="https://..." /></label>
              <label><span>作者</span><input value={form.author} onChange={e => setForm({ ...form, author: e.target.value })} /></label>
              <label><span>标签（逗号分隔）</span><input value={form.tags} onChange={e => setForm({ ...form, tags: e.target.value })} placeholder="增长, 运营, 策略" /></label>
              <label className="wide"><span>描述</span><textarea rows={3} value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></label>
              <label className="wide"><span>系统提示词</span><textarea rows={6} value={form.systemPrompt} onChange={e => setForm({ ...form, systemPrompt: e.target.value })} placeholder="定义专家身份、能力边界、输出格式。" /></label>
              <label className="wide"><span>Quick Prompts（每行一条）</span><textarea rows={4} value={form.quickPrompts} onChange={e => setForm({ ...form, quickPrompts: e.target.value })} /></label>
            </div>
            <div className="ed-footer">
              <span className="ed-footer-hint">自定义专家会保存到当前账号。</span>
              <button className="ed-summon" type="button" disabled={saving || !form.name || !form.profession || !form.description} onClick={submitForm}>{saving ? '保存中...' : '保存专家'}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}


type SkillMarketplaceViewProps = {
  query: string
  installedOnly: boolean
  createOpen: boolean
  onCreateClose: () => void
  onInstalledCountChange: (count: number) => void
}

const CURATED_SKILL_SOURCE = 'qianyuan-curated-template'

type CuratedSkillTemplate = SkillMarketEntryDto & {
  body: string
  icon: string
}

const SKILL_CATEGORY_LABELS: Record<string, string> = {
  opc: 'OPC 生产力',
  office: '办公协作',
  development: '研发工具',
  information: '信息检索',
  education: '教育学习',
  data: '数据处理',
  web: '浏览器增强',
  finance: '金融分析',
  document: '文档处理',
  writing: '内容创作',
  automation: '自动化',
  productivity: '效率工具',
  visualization: '可视化',
  research: '研究分析',
  general: '通用',
}

const CURATED_SKILL_TEMPLATES: CuratedSkillTemplate[] = [
  {
    id: 'curated:qy-cover-design', packageId: 'featured', packageName: '精选技能', name: 'QIANYUAN 爆款封面设计',
    description: '面向运营、课程、短视频场景，快速生成高点击率封面方案。',
    category: 'opc', tags: ['封面', '设计', '内容创作'], triggerPhrases: ['设计封面', '生成封面', '爆款封面'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: '封',
    body: 'Use this skill when the user needs a high-click visual cover concept. Clarify target audience, platform, copy, tone, and constraints. Output: 1) visual direction, 2) three layout options, 3) headline copy variants, 4) image prompt, 5) production checklist.',
  },
  {
    id: 'curated:qy-cloud-files', packageId: 'featured', packageName: '精选技能', name: 'QIANYUAN 云文件助手',
    description: '围绕本地与云端文件的查找、整理、摘要和归档提供操作方案。',
    category: 'office', tags: ['文件', '云盘', '整理'], triggerPhrases: ['整理文件', '查找文件', '归档资料'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: '云',
    body: 'Use this skill when the user needs to organize, locate, summarize, or archive files. Ask for storage location and naming rules, then produce a safe file plan, search patterns, summary fields, and rollback notes.',
  },
  {
    id: 'curated:qy-survey', packageId: 'featured', packageName: '精选技能', name: 'QIANYUAN 问卷设计',
    description: '从调研目标反推题目结构、选项设计、样本策略和结果分析。',
    category: 'office', tags: ['问卷', '调研', '分析'], triggerPhrases: ['设计问卷', '调研问题', '问卷分析'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: '问',
    body: 'Use this skill to design surveys. Start from research objective and respondent profile. Output questionnaire sections, question wording, option design, sampling advice, bias checks, and analysis plan.',
  },
  {
    id: 'curated:neodata-finance-search', packageId: 'recommended', packageName: '推荐技能套件', name: 'NeoData 金融搜索服务',
    description: '为金融研报、公告、市场新闻和数据线索整理检索策略。',
    category: 'finance', tags: ['金融', '搜索', '研报'], triggerPhrases: ['金融搜索', '查研报', '公告检索'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: '搜',
    body: 'Use this skill for finance information discovery. Clarify market, ticker, time range, and evidence needs. Provide search queries, source priority, extraction fields, and confidence notes.',
  },
  {
    id: 'curated:finance-database-query', packageId: 'recommended', packageName: '推荐技能套件', name: '金融数据库查询助手',
    description: '把金融数据需求拆成指标、口径、时间窗和查询步骤。',
    category: 'finance', tags: ['数据库', '指标', '查询'], triggerPhrases: ['查询金融数据', '查指标', '数据库取数'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: '数',
    body: 'Use this skill to plan finance database queries. Define entity, metric, period, frequency, adjustment rules, filters, and expected output table. Flag missing permissions or unclear definitions.',
  },
  {
    id: 'curated:market-intelligence-search', packageId: 'recommended', packageName: '推荐技能套件', name: '市场情报搜索',
    description: '聚合竞品、行业、客户和政策线索，形成可执行情报简报。',
    category: 'information', tags: ['市场', '竞品', '情报'], triggerPhrases: ['市场情报', '竞品分析', '行业搜索'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: '情',
    body: 'Use this skill for market intelligence. Structure findings by competitors, customers, policy, trend, and risks. Separate facts from assumptions and list next evidence to collect.',
  },
  {
    id: 'curated:markdown-converter', packageId: 'recommended', packageName: '推荐技能套件', name: 'Markdown 文档转换',
    description: '将 PDF、Word、PPT 或 OCR 文本整理为结构清晰的 Markdown。',
    category: 'document', tags: ['Markdown', 'OCR', '文档'], triggerPhrases: ['转 Markdown', '文档转换', 'OCR 整理'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: 'M',
    body: 'Use this skill to convert documents into clean Markdown. Preserve headings, tables, lists, code blocks, and citations. Note uncertain OCR text and propose cleanup steps.',
  },
  {
    id: 'curated:stock-diagnosis', packageId: 'recommended', packageName: '推荐技能套件', name: '股票综合诊断',
    description: '从基本面、资金面、技术面和风险事件生成股票分析框架。',
    category: 'finance', tags: ['股票', '诊断', '风险'], triggerPhrases: ['股票诊断', '分析股票', '投资风险'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: '股',
    body: 'Use this skill for stock analysis frameworks. Cover fundamentals, valuation, capital flow, technical signals, catalysts, risks, and scenarios. Do not provide personalized financial advice.',
  },
  {
    id: 'curated:music-assistant', packageId: 'recommended', packageName: '推荐技能套件', name: '音乐助手',
    description: '辅助音乐灵感、歌单策划、歌词结构和 AI 音乐提示词编写。',
    category: 'information', tags: ['音乐', '创意', '提示词'], triggerPhrases: ['音乐灵感', '写歌词', '歌单策划'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: '音',
    body: 'Use this skill for music ideation. Clarify genre, mood, tempo, audience, and use case. Output structure, references, lyric outline, playlist logic, or generation prompt as needed.',
  },
  {
    id: 'curated:excel-processing', packageId: 'recommended', packageName: '推荐技能套件', name: 'Excel 文件处理',
    description: '处理 Excel 清洗、汇总、透视、公式设计和异常检查。',
    category: 'data', tags: ['Excel', '表格', '数据清洗'], triggerPhrases: ['处理 Excel', '表格汇总', '数据清洗'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: 'X',
    body: 'Use this skill for spreadsheet work. Identify sheets, columns, cleaning rules, formulas, pivot needs, validation checks, and expected output. Prefer reversible transformations.',
  },
  {
    id: 'curated:web-access', packageId: 'recommended', packageName: '推荐技能套件', name: 'Web Access 浏览器助手',
    description: '为网页访问、信息抽取、表单检查和页面自动化设计步骤。',
    category: 'web', tags: ['浏览器', '网页', '自动化'], triggerPhrases: ['访问网页', '网页提取', '浏览器操作'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: '网',
    body: 'Use this skill when browser-based work is needed. Define target pages, actions, data to extract, validation rules, and safe stopping conditions. Avoid destructive actions unless confirmed.',
  },
  {
    id: 'curated:skill-creator-guide', packageId: 'recommended', packageName: '推荐技能套件', name: '技能创建指南',
    description: '帮助用户把重复流程沉淀为可复用 SKILL.md 指令。',
    category: 'development', tags: ['技能', '创建', '流程'], triggerPhrases: ['创建技能', '写 SKILL', '沉淀流程'],
    source: CURATED_SKILL_SOURCE, sourceUrl: null, installed: false, installedSkillId: null, enabled: false, icon: '技',
    body: 'Use this skill to create a reusable skill. Capture trigger conditions, required context, step-by-step workflow, validation method, safety limits, and examples. Keep instructions concrete and testable.',
  },
]

function SkillMarketplaceView({ query, installedOnly, createOpen, onCreateClose, onInstalledCountChange }: SkillMarketplaceViewProps) {
  const [packages, setPackages] = useState<SkillPackageDto[]>([])
  const [categories, setCategories] = useState<SkillCategoryDto[]>([])
  const [installed, setInstalled] = useState<InstalledSkillDto[]>([])
  const [category, setCategory] = useState('')
  const [featureOffset, setFeatureOffset] = useState(0)
  const [busy, setBusy] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [createForm, setCreateForm] = useState<CreateSkillRequest>({ id: '', name: '', description: '', body: '', category: 'general', tags: [], triggerPhrases: [], scope: 'user' })
  const [tagText, setTagText] = useState('')
  const [triggerText, setTriggerText] = useState('')

  async function reload() {
    setLoading(true)
    setError(null)
    try {
      const [marketPackages, installedRows] = await Promise.all([
        listSkillMarket().catch(() => [] as SkillPackageDto[]),
        listInstalledSkills().catch(() => [] as InstalledSkillDto[]),
      ])
      const mergedPackages = mergeSkillPackages([...marketPackages, ...buildCuratedSkillPackages(installedRows)])
      const categoryRows = await listSkillCategories().catch(() => [] as SkillCategoryDto[])
      setPackages(mergedPackages)
      setCategories(mergeSkillCategories(categoryRows, buildSkillCategories(mergedPackages)))
      setInstalled(installedRows)
      onInstalledCountChange(installedRows.length)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { void reload() }, [])

  const filteredPackages = useMemo(() => filterSkillPackages(packages, category, query, installedOnly), [packages, category, query, installedOnly])
  const visibleEntries = useMemo(() => filteredPackages.flatMap(pkg => pkg.entries), [filteredPackages])
  const featured = useMemo(() => {
    const candidates = filterSkillPackages(packages, '', query, false).flatMap(pkg => pkg.entries).filter(entry => entry.source === CURATED_SKILL_SOURCE || entry.installed)
    const source = candidates.length > 0 ? candidates : visibleEntries
    if (source.length <= 3) return source
    return [0, 1, 2].map(i => source[(featureOffset + i) % source.length])
  }, [packages, query, visibleEntries, featureOffset])

  async function addSkill(entry: SkillMarketEntryDto) {
    if (entry.installed) return
    setBusy(entry.id)
    setError(null)
    try {
      const template = CURATED_SKILL_TEMPLATES.find(item => item.id === entry.id)
      if (template) {
        await createSkill({
          id: template.id.replace(/^curated:/, ''),
          name: template.name,
          description: template.description,
          body: template.body,
          category: template.category,
          tags: template.tags,
          triggerPhrases: template.triggerPhrases,
          scope: 'user',
        })
      } else {
        await installSkill({ marketEntryId: entry.id, enabled: true })
      }
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(null)
    }
  }

  async function submitCreate(event: FormEvent) {
    event.preventDefault()
    if (!createForm.name.trim() || !createForm.description.trim() || !createForm.body.trim()) return
    setBusy('create')
    setError(null)
    try {
      await createSkill({ ...createForm, tags: splitValues(tagText), triggerPhrases: splitValues(triggerText), scope: 'user' })
      setCreateForm({ id: '', name: '', description: '', body: '', category: 'general', tags: [], triggerPhrases: [], scope: 'user' })
      setTagText('')
      setTriggerText('')
      onCreateClose()
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(null)
    }
  }

  return <div className="market-body skill-market-body">
    {error && <div className="market-error">{error}</div>}
    <section className="skill-market-featured">
      <div className="skill-market-section-head">
        <h3>精选技能</h3>
        <button type="button" className="skill-market-link" onClick={() => setFeatureOffset(v => v + 3)}>换一换</button>
      </div>
      <div className="skill-market-featured-grid">
        {featured.map(entry => <SkillCard key={entry.id} entry={entry} busy={busy === entry.id} featured onInstall={() => addSkill(entry)} />)}
        {!loading && featured.length === 0 && <div className="market-empty-tab">暂无精选技能</div>}
      </div>
    </section>

    <section className="skill-market-recommend">
      <div className="skill-market-title-row">
        <h3>推荐 <span>技能</span> 套件</h3>
        <em>{installed.length} 个已安装</em>
      </div>
      <div className="skill-market-tabs">
        <button className={category === '' ? 'active' : ''} type="button" onClick={() => setCategory('')}>全部</button>
        {categories.map(item => <button key={item.id} className={category === item.id ? 'active' : ''} type="button" onClick={() => setCategory(item.id)}>{item.name}</button>)}
      </div>
      {loading && <div className="market-loading">加载中...</div>}
      {!loading && visibleEntries.length === 0 && <div className="market-empty-tab">{installedOnly ? '还没有安装符合条件的技能' : '没有找到匹配的技能'}</div>}
      {!loading && visibleEntries.length > 0 && <div className="skill-market-grid">
        {visibleEntries.map(entry => <SkillCard key={entry.id} entry={entry} busy={busy === entry.id} onInstall={() => addSkill(entry)} />)}
      </div>}
    </section>

    {createOpen && <div className="modal-backdrop expert-detail-backdrop" onClick={onCreateClose}>
      <form className="expert-detail-card skill-create-dialog" onSubmit={submitCreate} onClick={event => event.stopPropagation()}>
        <div className="modal-header"><strong>添加技能</strong><button className="ghost" type="button" onClick={onCreateClose}>关闭</button></div>
        <div className="custom-form-grid">
          <label><span>技能 ID（可选）</span><input value={createForm.id} onChange={e => setCreateForm({ ...createForm, id: e.target.value })} placeholder="report-writer" /></label>
          <label><span>名称</span><input value={createForm.name} onChange={e => setCreateForm({ ...createForm, name: e.target.value })} required /></label>
          <label><span>分类</span><select value={createForm.category ?? 'general'} onChange={e => setCreateForm({ ...createForm, category: e.target.value })}><option value="general">通用</option>{categories.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
          <label><span>标签（逗号分隔）</span><input value={tagText} onChange={e => setTagText(e.target.value)} placeholder="写作, 总结" /></label>
          <label className="wide"><span>描述</span><textarea rows={3} value={createForm.description} onChange={e => setCreateForm({ ...createForm, description: e.target.value })} required /></label>
          <label className="wide"><span>触发词（逗号或换行）</span><input value={triggerText} onChange={e => setTriggerText(e.target.value)} placeholder="写报告, 总结材料" /></label>
          <label className="wide"><span>技能内容</span><textarea rows={7} value={createForm.body} onChange={e => setCreateForm({ ...createForm, body: e.target.value })} required placeholder="描述这个技能的使用场景、工作流程、输出格式和注意事项" /></label>
        </div>
        <div className="ed-footer"><span className="ed-footer-hint">创建后会生成本地 SKILL.md 并自动注册</span><button className="ed-summon" type="submit" disabled={busy === 'create'}>{busy === 'create' ? '创建中...' : '创建技能'}</button></div>
      </form>
    </div>}
  </div>
}

function buildCuratedSkillPackages(installed: InstalledSkillDto[]): SkillPackageDto[] {
  const rows = CURATED_SKILL_TEMPLATES.map(template => ({
    ...template,
    installed: isCuratedInstalled(template, installed),
    enabled: isCuratedInstalled(template, installed),
    installedSkillId: isCuratedInstalled(template, installed) ? `custom.${normalizeSkillId(template.id.replace(/^curated:/, ''))}` : null,
  }))
  const groups = new Map<string, CuratedSkillTemplate[]>()
  for (const row of rows) {
    const next = groups.get(row.packageId) ?? []
    next.push(row)
    groups.set(row.packageId, next)
  }
  return Array.from(groups.entries()).map(([id, entries], index) => ({
    id,
    name: entries[0]?.packageName ?? '技能套件',
    description: id === 'featured' ? '适合立即安装的精选技能模板。' : '覆盖常用场景的推荐技能模板。',
    category: entries[0]?.category ?? 'general',
    sortOrder: index,
    entries,
  }))
}

function mergeSkillPackages(packages: SkillPackageDto[]): SkillPackageDto[] {
  const packageMap = new Map<string, SkillPackageDto>()
  const seenEntries = new Set<string>()
  for (const pkg of packages) {
    const key = pkg.id || pkg.category || 'general'
    const existing = packageMap.get(key) ?? { ...pkg, entries: [] }
    for (const entry of pkg.entries ?? []) {
      if (seenEntries.has(entry.id)) continue
      seenEntries.add(entry.id)
      existing.entries.push(entry)
    }
    packageMap.set(key, existing)
  }
  return Array.from(packageMap.values()).filter(pkg => pkg.entries.length > 0).sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name))
}

function filterSkillPackages(packages: SkillPackageDto[], category: string, query: string, installedOnly: boolean): SkillPackageDto[] {
  const keyword = query.trim().toLowerCase()
  return packages.map(pkg => ({
    ...pkg,
    entries: pkg.entries.filter(entry => {
      if (installedOnly && !entry.installed) return false
      if (category && entry.category !== category) return false
      if (!keyword) return true
      const haystack = [entry.id, entry.name, entry.description, entry.category, ...(entry.tags ?? []), ...(entry.triggerPhrases ?? [])].join(' ').toLowerCase()
      return haystack.includes(keyword)
    }),
  })).filter(pkg => pkg.entries.length > 0)
}

function mergeSkillCategories(...groups: SkillCategoryDto[][]): SkillCategoryDto[] {
  const rows = new Map<string, SkillCategoryDto>()
  for (const group of groups) {
    for (const item of group) {
      if (!item.id || item.id === 'installed') continue
      const existing = rows.get(item.id)
      rows.set(item.id, {
        id: item.id,
        name: SKILL_CATEGORY_LABELS[item.id] ?? item.name ?? skillCategoryName(item.id),
        marketCount: Math.max(existing?.marketCount ?? 0, item.marketCount ?? 0),
        installedCount: Math.max(existing?.installedCount ?? 0, item.installedCount ?? 0),
      })
    }
  }
  const order = ['opc', 'office', 'development', 'information', 'education', 'data', 'web', 'finance', 'document', 'writing', 'automation', 'general']
  return Array.from(rows.values()).sort((a, b) => (order.indexOf(a.id) < 0 ? 99 : order.indexOf(a.id)) - (order.indexOf(b.id) < 0 ? 99 : order.indexOf(b.id)) || a.name.localeCompare(b.name))
}

function buildSkillCategories(packages: SkillPackageDto[]): SkillCategoryDto[] {
  const counts = new Map<string, { marketCount: number; installedCount: number }>()
  for (const entry of packages.flatMap(pkg => pkg.entries)) {
    const key = entry.category || 'general'
    const current = counts.get(key) ?? { marketCount: 0, installedCount: 0 }
    current.marketCount += 1
    if (entry.installed) current.installedCount += 1
    counts.set(key, current)
  }
  return Array.from(counts.entries()).map(([id, count]) => ({ id, name: skillCategoryName(id), ...count }))
}

function isCuratedInstalled(template: SkillMarketEntryDto, installed: InstalledSkillDto[]) {
  const normalizedId = normalizeSkillId(template.id.replace(/^curated:/, ''))
  return installed.some(row => row.marketEntryId === template.id || row.skillId === `custom.${normalizedId}` || row.name === template.name)
}

function normalizeSkillId(value: string) {
  return value.trim().toLowerCase().replace(/[^a-z0-9._-]+/g, '-').replace(/^[._-]+|[._-]+$/g, '') || 'skill'
}

function skillCategoryName(category: string) {
  return SKILL_CATEGORY_LABELS[category] ?? category
}

function SkillCard({ entry, busy, featured, onInstall }: { entry: SkillMarketEntryDto; busy: boolean; featured?: boolean; onInstall: () => void }) {
  return <article className={`skill-market-card ${featured ? 'featured' : ''}`} title={entry.description}>
    <div className="skill-market-card-head">
      <span className="skill-market-icon" style={{ ['--skill-color' as string]: skillColor(entry.category) }}>{skillIcon(entry)}</span>
      <button className="skill-market-add" type="button" disabled={busy || entry.installed} onClick={onInstall} title={entry.installed ? '已安装' : '安装'}>{entry.installed ? '✓' : busy ? '...' : '+'}</button>
    </div>
    <strong>{entry.name}</strong>
    <p>{entry.description}</p>
  </article>
}

function skillIcon(entry: SkillMarketEntryDto) {
  return CURATED_SKILL_TEMPLATES.find(item => item.id === entry.id)?.icon ?? skillInitial(entry.name)
}

function skillInitial(name: string) {
  const trimmed = name.trim()
  return trimmed ? trimmed.slice(0, 1).toUpperCase() : '+'
}

function skillColor(category: string) {
  const colors: Record<string, string> = {
    opc: '#16b978', office: '#3b82f6', development: '#8b5cf6', information: '#f59e0b', education: '#ef7b45', data: '#10a6a6', web: '#407cff', finance: '#0f9d76', document: '#64748b', writing: '#d5567b', automation: '#ef7b45', general: '#64748b',
  }
  return colors[category] ?? '#3b82f6'
}

function PromptButton({ index, text, onClick }: { index: number; text: string; onClick: () => void }) {
  return <button className="ed-prompt" type="button" onClick={onClick}>
    <span className="ed-prompt-idx">{index}</span><span className="ed-prompt-text">{text}</span><span className="ed-prompt-go">→</span>
  </button>
}

function splitValues(value: string) {
  return value.split(/[，,\n]/).map(v => v.trim()).filter(Boolean)
}

function splitLines(value: string) {
  return value.split(/\n+/).map(v => v.trim()).filter(Boolean)
}

function bannerAccent(categoryId: string) {
  const map: Record<string, string> = {
    '01-General': '#6f73ff', '02-ProductDesign': '#ff8a4c', '03-ResearchAnalysis': '#3c9a7d',
    '04-Development': '#3b82f6', '05-MarketingGrowth': '#d5567b', '06-ContentCreative': '#7c6cff',
    '07-DataAI': '#14a88b', '08-FinanceInvestment': '#0f9d76', '09-OperationsHR': '#e0a32b',
    '10-ProjectQuality': '#5b8def', '11-SecurityCompliance': '#c8873a', '12-IndustryConsultant': '#3a7bd5',
    'custom': '#17b981',
  }
  return map[categoryId] ?? '#5b6470'
}
