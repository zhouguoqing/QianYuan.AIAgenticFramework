import { useEffect, useState } from 'react'
import type { KnowledgeDocument, KnowledgeSearchResult } from '../types/api'
import { listKnowledge, uploadKnowledge, uploadKnowledgeFile, searchKnowledge, deleteKnowledge } from '../services/api'

export function KnowledgeManager({ onClose }: { onClose: () => void }) {
  const [tab, setTab] = useState<'manage'|'upload'|'search'>('manage')
  const [docs, setDocs] = useState<KnowledgeDocument[]>([])
  const [busy, setBusy] = useState(false)

  useEffect(() => { reload() }, [])
  function reload() { listKnowledge().then(setDocs).catch(() => {}) }

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()} style={{ width: 800 }}>
        <div className="modal-header">
          <strong>知识库管理</strong>
          <span style={{ flex: 1 }} />
          <button className="ghost" onClick={onClose}>关闭</button>
        </div>
        <div className="modal-tabs">
          <button className={tab === 'manage' ? 'active' : ''} onClick={() => setTab('manage')}>管理</button>
          <button className={tab === 'upload' ? 'active' : ''} onClick={() => setTab('upload')}>上传</button>
          <button className={tab === 'search' ? 'active' : ''} onClick={() => setTab('search')}>查询</button>
        </div>
        <div className="modal-body">
          {tab === 'manage' && <ManageTab docs={docs} onDeleted={() => { reload(); setTab('manage') }} />}
          {tab === 'upload' && <UploadTab onUploaded={() => { reload(); setTab('manage') }} />}
          {tab === 'search' && <SearchTab />}
        </div>
      </div>
    </div>
  )
}

function ManageTab({ docs, onDeleted }: { docs: KnowledgeDocument[]; onDeleted: () => void }) {
  async function del(id: string) {
    if (!confirm('删除该文档？')) return
    await deleteKnowledge(id)
    onDeleted()
  }
  return (
    <div>
      {docs.length === 0 && <div className="small">暂无文档</div>}
      {docs.map(d => (
        <div key={d.id} className="skill-card">
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <div>
              <strong>{d.title}</strong>
              <div className="small">{new Date(d.createdAt).toLocaleString()}</div>
              {d.sourceFile && <div className="small">来源: {d.sourceFile} {d.sourceSection ? `· ${d.sourceSection}` : ''}</div>}
            </div>
            <div>
              <button className="ghost" onClick={() => navigator.clipboard.writeText(d.id)}>复制 ID</button>
              <button className="link-btn" onClick={() => del(d.id)}>删除</button>
            </div>
          </div>
          <div style={{ marginTop: 8, whiteSpace: 'pre-wrap' }}>{d.content.slice(0, 800)}{d.content.length>800? '...':''}</div>
        </div>
      ))}
    </div>
  )
}

function UploadTab({ onUploaded }: { onUploaded: () => void }) {
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [tags, setTags] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit() {
    if (!file && !content.trim()) return alert('请提供文件或文本内容')
    setBusy(true)
    try {
      if (file) {
        const form = new FormData()
        form.append('file', file)
        if (title.trim()) form.append('title', title.trim())
        if (tags.trim()) form.append('tags', tags.trim())
        await uploadKnowledgeFile(form)
      } else {
        await uploadKnowledge({ title: title.trim(), content: content.trim(), tags: tags.split(',').map(s => s.trim()).filter(Boolean) })
      }
      setTitle(''); setContent(''); setTags(''); setFile(null)
      onUploaded()
    } catch (e:any) { alert(e.message || String(e)) } finally { setBusy(false) }
  }

  return (
    <div>
      <div className="small">支持上传 PDF、DOCX、PPTX、XLSX、Markdown、HTML、纯文本或图像文件。也可以直接粘贴内容。</div>
      <div style={{ marginTop: 8 }}>
        <div className="small">文件</div>
        <input type="file" accept=".pdf,.docx,.pptx,.xlsx,.md,.markdown,.html,.htm,.txt,.png,.jpg,.jpeg,.bmp,.tif,.tiff" onChange={e => setFile(e.target.files?.[0] ?? null)} />
        {file && <div className="small">已选: {file.name}</div>}
      </div>
      <div style={{ marginTop: 8 }}>
        <div className="small">标题</div>
        <input value={title} onChange={e => setTitle(e.target.value)} />
      </div>
      <div style={{ marginTop: 8 }}>
        <div className="small">标签 (逗号分隔)</div>
        <input value={tags} onChange={e => setTags(e.target.value)} />
      </div>
      <div style={{ marginTop: 8 }}>
        <div className="small">文本内容</div>
        <textarea rows={10} value={content} onChange={e => setContent(e.target.value)} placeholder={file ? '文件已选时会优先上传文件内容' : '直接粘贴文本...'} />
      </div>
      <div style={{ marginTop: 8 }}>
        <button onClick={submit} disabled={busy}>{busy ? '上传中…' : '上传并入库'}</button>
      </div>
    </div>
  )
}

function SearchTab() {
  const [q, setQ] = useState('')
  const [results, setResults] = useState<KnowledgeSearchResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [answer, setAnswer] = useState(false)

  async function run() {
    if (!q.trim()) return
    setBusy(true); setResults(null)
    try {
      const r = await searchKnowledge(q.trim(), 5, answer)
      setResults(r)
    } catch (e:any) { alert(e.message || String(e)) } finally { setBusy(false) }
  }

  return (
    <div>
      <div style={{ display: 'flex', gap: 8 }}>
        <input style={{ flex: 1 }} value={q} onChange={e => setQ(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') run() }} placeholder="输入查询" />
        <label style={{ alignSelf: 'center' }}><input type="checkbox" checked={answer} onChange={e => setAnswer(e.target.checked)} /> 让模型基于检索结果回答</label>
        <button onClick={run} disabled={busy || !q.trim()}>{busy ? '搜索中…' : '搜索'}</button>
      </div>

      {results && (
        <div style={{ marginTop: 12 }}>
          <div><strong>模型回答</strong></div>
          <pre style={{ whiteSpace: 'pre-wrap' }}>{results.answer ?? '(无)'}</pre>
          <div style={{ marginTop: 8 }}><strong>检索到的文档</strong></div>
          {results.matches.map(m => (
            <div key={m.id} className="skill-card">
              <strong>{m.title}</strong>
              <div className="small">{m.tags.join(', ')}{m.sourceFile ? ` · ${m.sourceFile}${m.sourceSection ? ` · ${m.sourceSection}` : ''}` : ''}</div>
              <div style={{ marginTop: 8, whiteSpace: 'pre-wrap' }}>{m.content.slice(0, 800)}{m.content.length > 800 ? '...' : ''}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
