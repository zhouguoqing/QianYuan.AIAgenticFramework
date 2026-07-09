import { useEffect, useMemo, useState } from 'react'
import type {
  ExpertCategoryDto, ExpertScenarioDto, ExpertSummaryDto, ExpertDetailDto,
} from '../types/api'
import {
  listExpertCategories, listExpertScenarios, listExperts, getExpert,
} from '../services/api'

type TopTab = 'experts' | 'skills' | 'connectors'
type ExpertKind = 'agent' | 'team'
type SortMode = 'hot' | 'newest'

interface Props {
  onBack: () => void
  onLaunch: (prompt: string, expert: ExpertDetailDto) => void
  onOpenSkills?: () => void
}

function initials(name: string): string {
  const trimmed = name.trim()
  return trimmed ? trimmed.slice(0, 1).toUpperCase() : '?'
}

function Avatar({ expert, size = 40 }: { expert: ExpertSummaryDto; size?: number }) {
  const [failed, setFailed] = useState(false)
  const style = { width: size, height: size, fontSize: size * 0.4 }
  if (failed || !expert.avatarUrl) {
    return <span className="expert-avatar fallback" style={style}>{initials(expert.name)}</span>
  }
  return (
    <img
      className="expert-avatar"
      style={style}
      src={expert.avatarUrl}
      alt={expert.name}
      loading="lazy"
      onError={() => setFailed(true)}
    />
  )
}

export function ExpertMarketplace({ onBack, onLaunch, onOpenSkills }: Props) {
  const [topTab, setTopTab] = useState<TopTab>('experts')
  const [kind, setKind] = useState<ExpertKind>('agent')
  const [sort, setSort] = useState<SortMode>('hot')
  const [category, setCategory] = useState<string>('all')

  const [categories, setCategories] = useState<ExpertCategoryDto[]>([])
  const [scenarios, setScenarios] = useState<ExpertScenarioDto[]>([])
  const [experts, setExperts] = useState<ExpertSummaryDto[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [detail, setDetail] = useState<ExpertDetailDto | null>(null)
  const [detailBusy, setDetailBusy] = useState(false)

  useEffect(() => {
    Promise.all([listExpertCategories(), listExpertScenarios()])
      .then(([cats, scens]) => { setCategories(cats); setScenarios(scens) })
      .catch(err => setError(err instanceof Error ? err.message : String(err)))
  }, [])

  useEffect(() => {
    let alive = true
    setLoading(true)
    setError(null)
    listExperts({ category, type: kind, sort })
      .then(res => {
        if (!alive) return
        setExperts(res.items)
        setTotal(res.total)
      })
      .catch(err => { if (alive) setError(err instanceof Error ? err.message : String(err)) })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [category, kind, sort])

  const categoryChips = useMemo(
    () => [{ id: 'all', name: '全部', description: '', count: total }, ...categories],
    [categories, total],
  )

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

  return (
    <div className="expert-market">
      <header className="market-topbar">
        <div className="market-tabs">
          <button className="market-back" type="button" onClick={onBack} title="返回">‹</button>
          <button className={`market-tab ${topTab === 'experts' ? 'active' : ''}`} type="button" onClick={() => setTopTab('experts')}>
            <span className="mt-ico">☷</span>专家
          </button>
          <button className={`market-tab ${topTab === 'skills' ? 'active' : ''}`} type="button" onClick={() => { setTopTab('skills'); onOpenSkills?.() }}>
            <span className="mt-ico">◆</span>技能
          </button>
          <button className={`market-tab ${topTab === 'connectors' ? 'active' : ''}`} type="button" onClick={() => setTopTab('connectors')}>
            <span className="mt-ico">⚭</span>连接器
          </button>
        </div>
        <button className="market-mine" type="button">◇ 我的专家</button>
      </header>

      {topTab !== 'experts' ? (
        <div className="market-body">
          <div className="market-empty-tab">
            {topTab === 'skills' ? '技能管理已在“专家·技能·连接器”工作台打开。' : '连接器市场即将上线。'}
          </div>
        </div>
      ) : (
        <div className="market-body">
          {scenarios.length > 0 && (
            <section className="market-featured">
              <h3>精选场景</h3>
              <div className="scenario-scroller">
                {scenarios.map(sc => (
                  <div key={sc.id} className="scenario-card" style={{ ['--accent' as string]: sc.accent }}>
                    <div className="scenario-head">
                      <strong>{sc.name}</strong>
                    </div>
                    <div className="scenario-experts">
                      {sc.experts.map(e => (
                        <button key={e.id} className="scenario-expert" type="button" onClick={() => openDetail(e.id)} title={e.profession}>
                          <Avatar expert={e} size={26} />
                          <span>{e.profession}</span>
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
                <button className={sort === 'hot' ? 'active' : ''} type="button" onClick={() => setSort('hot')}>最热</button>
                <button className={sort === 'newest' ? 'active' : ''} type="button" onClick={() => setSort('newest')}>最新</button>
              </div>
            </div>

            <div className="category-chips">
              {categoryChips.map(c => (
                <button
                  key={c.id}
                  className={`chip ${category === c.id ? 'active' : ''}`}
                  type="button"
                  onClick={() => setCategory(c.id)}
                >
                  {c.name}
                </button>
              ))}
            </div>

            {error && <div className="market-error">{error}</div>}
            {loading && experts.length === 0 && <div className="market-loading">加载中…</div>}
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
                      </div>
                      <span className="ec-profession">{e.profession}</span>
                    </div>
                  </div>
                  <p className="ec-desc">{e.description}</p>
                  <div className="ec-tags">
                    {e.tags.slice(0, 3).map(t => <span key={t}>{t}</span>)}
                  </div>
                </button>
              ))}
            </div>
          </section>
        </div>
      )}

      {(detail || detailBusy) && (
        <div className="modal-backdrop expert-detail-backdrop" onClick={() => setDetail(null)}>
          <div className="expert-detail-card" onClick={e => e.stopPropagation()}>
            {detailBusy && !detail ? (
              <div className="market-loading">加载中…</div>
            ) : detail && (
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
                      </div>
                      <span className="ed-profession">{detail.profession}</span>
                      <div className="ed-meta">
                        <span className="ed-meta-item">{detail.categoryName}</span>
                        {detail.author && <span className="ed-meta-item">by {detail.author}</span>}
                      </div>
                    </div>
                  </div>
                </div>

                <div className="ed-scroll">
                  <p className="ed-desc">{detail.description}</p>
                  {detail.tags.length > 0 && (
                    <div className="ec-tags ed-tags">
                      {detail.tags.map(t => <span key={t}>{t}</span>)}
                    </div>
                  )}

                  <div className="ed-prompts">
                    <h4>快速开始</h4>
                    {detail.quickPrompts.length === 0 && detail.defaultInitPrompt && (
                      <button className="ed-prompt" type="button" onClick={() => launch(detail.defaultInitPrompt)}>
                        <span className="ed-prompt-idx">1</span>
                        <span className="ed-prompt-text">{detail.defaultInitPrompt}</span>
                        <span className="ed-prompt-go">↗</span>
                      </button>
                    )}
                    {detail.quickPrompts.map((p, i) => (
                      <button key={i} className="ed-prompt" type="button" onClick={() => launch(p)}>
                        <span className="ed-prompt-idx">{i + 1}</span>
                        <span className="ed-prompt-text">{p}</span>
                        <span className="ed-prompt-go">↗</span>
                      </button>
                    ))}
                  </div>
                </div>

                <div className="ed-footer">
                  <span className="ed-footer-hint">召唤后可在输入框继续编辑再发送</span>
                  <button
                    className="ed-summon"
                    type="button"
                    onClick={() => launch(detail.defaultInitPrompt || `你好，我需要${detail.profession}的帮助。`)}
                  >
                    召唤专家
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

const CATEGORY_ACCENTS: Record<string, string> = {
  '06-ContentCreative': '#7c6cff',
  '08-FinanceInvestment': '#0f9d76',
  '11-SecurityCompliance': '#c8873a',
  '12-IndustryConsultant': '#3a7bd5',
  '05-MarketingGrowth': '#d5567b',
  '02-Engineering': '#3a7bd5',
  '01-ProductDesign': '#d5567b',
  '04-DataAI': '#0f9d76',
  '03-GameSpatial': '#7c6cff',
  '13-TencentZone': '#2f7bd6',
}

function bannerAccent(categoryId: string): string {
  return CATEGORY_ACCENTS[categoryId] ?? '#5b6470'
}
