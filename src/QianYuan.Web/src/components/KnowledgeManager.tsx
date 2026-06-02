import { useEffect, useMemo, useState } from 'react'
import type { KnowledgeDocument, KnowledgeSearchResult } from '../types/api'
import { listKnowledge, uploadKnowledge, uploadKnowledgeFile, searchKnowledge, deleteKnowledge } from '../services/api'

type Tab = 'documents' | 'create' | 'import' | 'recall' | 'ask'

interface Props {
  provider?: string | null
  onClose: () => void
}

export function KnowledgeManager({ provider, onClose }: Props) {
  const [tab, setTab] = useState<Tab>('documents')
  const [docs, setDocs] = useState<KnowledgeDocument[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { reload() }, [])

  async function reload() {
    setLoading(true)
    setError(null)
    try { setDocs(await listKnowledge()) }
    catch (e: any) { setError(e.message || String(e)) }
    finally { setLoading(false) }
  }

  const stats = useMemo(() => {
    const tags = new Set(docs.flatMap(d => d.tags ?? []))
    const characters = docs.reduce((sum, doc) => sum + (doc.content?.length ?? 0), 0)
    return { documents: docs.length, tags: tags.size, characters }
  }, [docs])

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal knowledge-modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <strong>知识库管理</strong>
          <span style={{ flex: 1 }} />
          <button className="ghost" onClick={onClose}>关闭</button>
        </div>
        <div className="knowledge-stats">
          <Metric label="文档" value={String(stats.documents)} />
          <Metric label="标签" value={String(stats.tags)} />
          <Metric label="字符" value={stats.characters.toLocaleString()} />
          <Metric label="问答 Provider" value={provider || '默认'} />
        </div>
        <div className="modal-tabs">
          <button className={tab === 'documents' ? 'active' : ''} onClick={() => setTab('documents')}>文档</button>
          <button className={tab === 'create' ? 'active' : ''} onClick={() => setTab('create')}>创建</button>
          <button className={tab === 'import' ? 'active' : ''} onClick={() => setTab('import')}>导入</button>
          <button className={tab === 'recall' ? 'active' : ''} onClick={() => setTab('recall')}>召回</button>
          <button className={tab === 'ask' ? 'active' : ''} onClick={() => setTab('ask')}>查询</button>
        </div>
        <div className="modal-body">
          {error && <div className="alert-error">{error}</div>}
          {tab === 'documents' && <DocumentsTab docs={docs} loading={loading} onReload={reload} onDeleted={() => { reload(); setTab('documents') }} />}
          {tab === 'create' && <CreateTab onCreated={() => { reload(); setTab('documents') }} />}
          {tab === 'import' && <ImportTab onImported={() => { reload(); setTab('documents') }} />}
          {tab === 'recall' && <RecallTab answer={false} provider={provider} />}
          {tab === 'ask' && <RecallTab answer provider={provider} />}
        </div>
      </div>
    </div>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="knowledge-metric">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

function DocumentsTab({ docs, loading, onReload, onDeleted }: { docs: KnowledgeDocument[]; loading: boolean; onReload: () => void; onDeleted: () => void }) {
  const [filter, setFilter] = useState('')
  const filtered = useMemo(() => {
    const q = filter.trim().toLowerCase()
    if (!q) return docs
    return docs.filter(d => [d.title, d.content, d.sourceFile, d.sourceSection, ...(d.tags ?? [])].join(' ').toLowerCase().includes(q))
  }, [docs, filter])

  async function del(id: string) {
    if (!confirm('删除该文档？')) return
    await deleteKnowledge(id)
    onDeleted()
  }

  return (
    <div>
      <div className="knowledge-toolbar">
        <input value={filter} onChange={e => setFilter(e.target.value)} placeholder="按标题、标签、内容过滤" />
        <button className="secondary-btn" onClick={onReload}>刷新</button>
      </div>
      {loading && <div className="small">加载中...</div>}
      {!loading && docs.length === 0 && <EmptyState title="暂无文档" detail="通过创建或导入把资料加入知识库。" />}
      {!loading && docs.length > 0 && filtered.length === 0 && <EmptyState title="没有匹配结果" detail="换一个关键词试试。" />}
      {filtered.map(doc => <DocumentCard key={doc.id} doc={doc} onDelete={() => del(doc.id)} />)}
    </div>
  )
}

function DocumentCard({ doc, onDelete }: { doc: KnowledgeDocument; onDelete: () => void }) {
  const source = [doc.sourceFile, doc.sourceSection].filter(Boolean).join(' · ')
  return (
    <div className="skill-card knowledge-doc-card">
      <div className="knowledge-card-head">
        <div>
          <strong>{doc.title || '(未命名)'}</strong>
          <div className="small">{new Date(doc.createdAt).toLocaleString()}</div>
          {source && <div className="small">来源: {source}</div>}
        </div>
        <div className="knowledge-actions">
          <button className="secondary-btn" onClick={() => navigator.clipboard.writeText(doc.id)}>复制 ID</button>
          <button className="danger-btn" onClick={onDelete}>删除</button>
        </div>
      </div>
      {doc.tags.length > 0 && (
        <div className="knowledge-tags">
          {doc.tags.map(tag => <span key={tag}>{tag}</span>)}
        </div>
      )}
      <div className="knowledge-excerpt">{excerpt(doc.content, 900)}</div>
    </div>
  )
}

function CreateTab({ onCreated }: { onCreated: () => void }) {
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [tags, setTags] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit() {
    if (!content.trim()) return alert('请输入文本内容')
    setBusy(true)
    try {
      await uploadKnowledge({ title: title.trim() || '(untitled)', content: content.trim(), tags: parseTags(tags) })
      setTitle('')
      setContent('')
      setTags('')
      onCreated()
    } catch (e: any) { alert(e.message || String(e)) }
    finally { setBusy(false) }
  }

  return (
    <div className="knowledge-form">
      <Field label="标题">
        <input value={title} onChange={e => setTitle(e.target.value)} placeholder="例如: 产品 FAQ" />
      </Field>
      <Field label="标签">
        <input value={tags} onChange={e => setTags(e.target.value)} placeholder="多个标签用逗号分隔" />
      </Field>
      <Field label="内容">
        <textarea rows={14} value={content} onChange={e => setContent(e.target.value)} placeholder="粘贴要入库的文本、规则、FAQ 或片段" />
      </Field>
      <div className="knowledge-submit-row">
        <span className="small">{content.trim().length.toLocaleString()} 字符</span>
        <button onClick={submit} disabled={busy || !content.trim()}>{busy ? '创建中...' : '创建知识文档'}</button>
      </div>
    </div>
  )
}

function ImportTab({ onImported }: { onImported: () => void }) {
  const [title, setTitle] = useState('')
  const [tags, setTags] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<string | null>(null)

  async function submit() {
    if (!file) return alert('请选择要导入的文件')
    setBusy(true)
    setResult(null)
    try {
      const form = new FormData()
      form.append('file', file)
      if (title.trim()) form.append('title', title.trim())
      if (tags.trim()) form.append('tags', tags.trim())
      const response = await uploadKnowledgeFile(form) as { documents?: KnowledgeDocument[] }
      setResult(`已导入 ${(response.documents?.length ?? 1).toLocaleString()} 个文档片段`)
      setTitle('')
      setTags('')
      setFile(null)
      onImported()
    } catch (e: any) { alert(e.message || String(e)) }
    finally { setBusy(false) }
  }

  return (
    <div className="knowledge-form">
      <div className="knowledge-import-box">
        <strong>选择文件导入</strong>
        <span className="small">支持 PDF、Office、Markdown、HTML、纯文本和常见图片格式。</span>
        <input type="file" accept=".pdf,.docx,.pptx,.xlsx,.md,.markdown,.html,.htm,.txt,.png,.jpg,.jpeg,.bmp,.tif,.tiff" onChange={e => setFile(e.target.files?.[0] ?? null)} />
        {file && <div className="small">已选: {file.name}</div>}
      </div>
      <Field label="标题覆盖">
        <input value={title} onChange={e => setTitle(e.target.value)} placeholder="留空则使用文件名" />
      </Field>
      <Field label="标签">
        <input value={tags} onChange={e => setTags(e.target.value)} placeholder="多个标签用逗号分隔" />
      </Field>
      <div className="knowledge-submit-row">
        {result ? <span className="small">{result}</span> : <span />}
        <button onClick={submit} disabled={busy || !file}>{busy ? '导入中...' : '导入知识库'}</button>
      </div>
    </div>
  )
}

function RecallTab({ answer, provider }: { answer: boolean; provider?: string | null }) {
  const [q, setQ] = useState('')
  const [topK, setTopK] = useState(5)
  const [results, setResults] = useState<KnowledgeSearchResult | null>(null)
  const [busy, setBusy] = useState(false)

  async function run() {
    if (!q.trim()) return
    setBusy(true)
    setResults(null)
    try { setResults(await searchKnowledge(q.trim(), topK, answer, provider ?? undefined)) }
    catch (e: any) { alert(e.message || String(e)) }
    finally { setBusy(false) }
  }

  return (
    <div>
      <div className="knowledge-searchbar">
        <input value={q} onChange={e => setQ(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') run() }} placeholder={answer ? '输入问题，基于召回内容生成回答' : '输入关键词或问题，查看召回片段'} />
        <label className="topk-control">
          <span>TopK</span>
          <input type="number" min={1} max={20} value={topK} onChange={e => setTopK(clampTopK(e.target.value))} />
        </label>
        <button onClick={run} disabled={busy || !q.trim()}>{busy ? '处理中...' : answer ? '查询' : '召回'}</button>
      </div>

      {results && (
        <div className="knowledge-results">
          {answer && (
            <div className="knowledge-answer">
              <div className="field-label">模型回答</div>
              <pre>{results.answer || '未生成回答。请确认 Provider 可用，或只查看下方召回结果。'}</pre>
            </div>
          )}
          <div className="field-label">召回结果 {results.matches.length}</div>
          {results.matches.length === 0 && <EmptyState title="暂无召回" detail="知识库里没有找到相关内容。" />}
          {results.matches.map(match => (
            <div key={match.id} className="skill-card knowledge-result-card">
              <div className="knowledge-card-head">
                <div>
                  <strong>{match.title || '(未命名)'}</strong>
                  <div className="small">{[...(match.tags ?? []), match.sourceFile, match.sourceSection].filter(Boolean).join(' · ') || '无标签'}</div>
                </div>
                <button className="secondary-btn" onClick={() => navigator.clipboard.writeText(match.id)}>复制 ID</button>
              </div>
              <div className="knowledge-excerpt">{excerpt(match.content, 1000)}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function EmptyState({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="knowledge-empty">
      <strong>{title}</strong>
      <span>{detail}</span>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="knowledge-field">
      <div className="small">{label}</div>
      {children}
    </div>
  )
}

function parseTags(tags: string) {
  return tags.split(',').map(tag => tag.trim()).filter(Boolean)
}

function excerpt(content: string, maxLength: number) {
  if (content.length <= maxLength) return content
  return `${content.slice(0, maxLength)}...`
}

function clampTopK(value: string) {
  const next = Number(value)
  if (!Number.isFinite(next)) return 5
  return Math.min(20, Math.max(1, Math.trunc(next)))
}
