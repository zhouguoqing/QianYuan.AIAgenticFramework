import { useEffect, useMemo, useRef, useState } from 'react'
import type { AgentDto, ComposerMode, ImagePart, ProviderDto, SkillManifestDto, WorkspaceContext } from '../types/api'
import { listAgents, listProviders, listSkills, parseKnowledgeFile } from '../services/api'

interface Props {
  busy: boolean
  onSubmit: (text: string, images: ImagePart[], mode: ComposerMode, workspace?: WorkspaceContext) => void
  onAbort: () => void
  selectedAgent?: string | null
  onAgentChange?: (id: string | null) => void
  selectedProvider?: string | null
  onProviderChange?: (id: string | null) => void
  selectedModel?: string | null
  onModelChange?: (model: string | null) => void
  selectedSkills?: string[]
  onSkillsChange?: (skills: string[]) => void
  onOpenSkillManager?: () => void
  activeExpert?: { id: string; name: string; avatarUrl: string; profession: string } | null
  onClearExpert?: () => void
  seedText?: string
  seedNonce?: number
}

type Panel = 'add' | 'mode' | 'expert' | 'skill' | 'model' | 'workspace' | 'permission' | null
type Attachment = { url: string; mime: string; name: string; size: number; file?: File }
type WorkspaceRoot = { id: string; label: string; path: string; writable: boolean; source: 'builtin' | 'selected' }

const permissionOptions = [
  { id: 'full', label: '允许完全访问' },
  { id: 'readonly', label: '只读' },
  { id: 'confirm', label: '操作前确认' },
]

export function Composer({
  busy, onSubmit, onAbort,
  selectedAgent = null, onAgentChange,
  selectedProvider = null, onProviderChange,
  selectedModel = null, onModelChange,
  selectedSkills = [], onSkillsChange,
  onOpenSkillManager,
  activeExpert = null, onClearExpert,
  seedText = '', seedNonce = 0,
}: Props) {
  const [text, setText] = useState('')
  const [attachments, setAttachments] = useState<Attachment[]>([])
  const [parsingAttachments, setParsingAttachments] = useState(false)
  const [mode, setMode] = useState<ComposerMode>('chat')
  const [panel, setPanel] = useState<Panel>(null)
  const [agents, setAgents] = useState<AgentDto[]>([])
  const [providers, setProviders] = useState<ProviderDto[]>([])
  const [skills, setSkills] = useState<SkillManifestDto[]>([])
  const [workspace, setWorkspace] = useState(() => localStorage.getItem('workpartner.workspace') ?? '')
  const [workspaceLabel, setWorkspaceLabel] = useState(() => localStorage.getItem('workpartner.workspaceLabel') ?? 'QianYuan.AgenticFramew…')
  const [workspaceSearch, setWorkspaceSearch] = useState('')
  const [permission, setPermission] = useState(() => localStorage.getItem('workpartner.permission') ?? 'full')
  const [workspaceRoots, setWorkspaceRoots] = useState<WorkspaceRoot[]>([])
  const ref = useRef<HTMLTextAreaElement>(null)
  const fileRef = useRef<HTMLInputElement>(null)

  useEffect(() => { ref.current?.focus({ preventScroll: true }) }, [])

  // Seed the composer text when a caller (e.g. summoning an expert) requests it.
  useEffect(() => {
    if (seedNonce <= 0) return
    setText(seedText)
    const el = ref.current
    if (el) {
      requestAnimationFrame(() => {
        el.focus({ preventScroll: true })
        el.style.height = 'auto'
        el.style.height = Math.min(220, el.scrollHeight) + 'px'
        const end = el.value.length
        el.setSelectionRange(end, end)
      })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [seedNonce])

  useEffect(() => {
    Promise.allSettled([listAgents(), listProviders(), listSkills()]).then(([agentResult, providerResult, skillResult]) => {
      if (agentResult.status === 'fulfilled') setAgents(agentResult.value)
      if (providerResult.status === 'fulfilled') setProviders(providerResult.value.providers)
      if (skillResult.status === 'fulfilled') setSkills(skillResult.value)
    }).catch(console.error)

    const getRoots = window.workpartner?.fileSystem?.getRoots
    if (getRoots) {
      void getRoots()
        .then(roots => {
          setWorkspaceRoots(roots)
          const hit = workspace ? roots.find(x => x.id === workspace) : undefined
          if (hit) {
            setWorkspaceLabel(hit.label || hit.path)
            return
          }
          if (!workspace && roots.length > 0) {
            setWorkspace(roots[0].id)
            setWorkspaceLabel(roots[0].label || roots[0].path)
          }
        })
        .catch(() => undefined)
    }
  }, [])

  useEffect(() => { localStorage.setItem('workpartner.workspace', workspace) }, [workspace])
  useEffect(() => { localStorage.setItem('workpartner.workspaceLabel', workspaceLabel) }, [workspaceLabel])
  useEffect(() => { localStorage.setItem('workpartner.permission', permission) }, [permission])

  const selectedAgentName = agents.find(a => a.id === selectedAgent)?.name ?? '专家'
  const permissionLabel = permissionOptions.find(x => x.id === permission)?.label ?? '允许完全访问'
  const modelLabel = selectedModel ? selectedModel : 'Auto · 云端'

  const modelOptions = useMemo(() => providers.flatMap(provider =>
    provider.models.map(modelName => ({ providerId: provider.providerId, modelName }))
  ), [providers])

  const visibleSkills = useMemo(() => skills, [skills])

  function autoSize(el: HTMLTextAreaElement) {
    el.style.height = 'auto'
    el.style.height = Math.min(220, el.scrollHeight) + 'px'
  }

  function pickFile(file: File) {
    const reader = new FileReader()
    reader.onload = () => {
      const url = reader.result as string
      setAttachments(prev => [...prev, { url, mime: file.type || 'application/octet-stream', name: file.name, size: file.size, file }])
    }
    reader.readAsDataURL(file)
  }

  async function submit() {
    const t = text.trim()
    if (!canSubmit(text, attachments.length, mode)) return
    const imageAttachments = attachments.filter(file => file.mime.startsWith('image/'))
    const fileAttachments = attachments.filter(file => !file.mime.startsWith('image/'))
    const parts: ImagePart[] = imageAttachments.map(i => ({ url: i.url, mime: i.mime, name: i.name, size: i.size }))
    const selectedRoot = workspaceRoots.find(root => root.id === workspace)
    const workspaceCtx: WorkspaceContext | undefined = selectedRoot || workspace || permission
      ? {
          workspaceId: workspace || undefined,
          workspacePath: selectedRoot?.path,
          workspaceLabel,
          permission,
        }
      : undefined

    setParsingAttachments(true)
    try {
      const enrichedText = await appendAttachmentAnalysis(t, fileAttachments)
      onSubmit(enrichedText, parts, mode, workspaceCtx)
    } catch {
      onSubmit(appendAttachmentSummary(t, fileAttachments), parts, mode, workspaceCtx)
    } finally {
      setParsingAttachments(false)
    }
    setText('')
    setAttachments([])
    if (ref.current) ref.current.style.height = '60px'
  }

  function setSelectedModel(providerId: string | null, modelName: string | null) {
    onProviderChange?.(providerId)
    onModelChange?.(modelName)
    setPanel(null)
  }

  function toggleSkill(skillId: string) {
    const next = selectedSkills.includes(skillId)
      ? selectedSkills.filter(id => id !== skillId)
      : [...selectedSkills, skillId]
    onSkillsChange?.(next)
  }

  function chooseWorkspace(id: string) {
    setWorkspace(id)
    const hit = workspaceRoots.find(x => x.id === id)
    if (hit) setWorkspaceLabel(hit.label || hit.path)
    setPanel(null)
  }

  async function chooseLocalWorkspace() {
    const picker = window.workpartner?.fileSystem?.selectDirectory
    if (!picker) {
      setPanel(null)
      return
    }
    const root = await picker()
    if (!root) return
    setWorkspaceRoots(prev => {
      const next = prev.filter(item => item.id !== root.id)
      next.unshift(root)
      return next
    })
    setWorkspace(root.id)
    setWorkspaceLabel(root.label || root.path || '本地工作空间')
    setWorkspaceSearch('')
    setPanel(null)
  }

  function choosePermission(id: string) {
    setPermission(id)
    setPanel(null)
  }

  function openFiles() {
    fileRef.current?.click()
    setPanel(null)
  }

  function onKey(e: React.KeyboardEvent) {
    // Enter sends. Shift+Enter (or IME composition) inserts a newline.
    if (e.key === 'Enter' && !e.shiftKey && !(e.nativeEvent as any).isComposing) {
      e.preventDefault(); submit()
    }
  }

  return (
    <div className="composer workpartner-composer">
      <div className="composer-row">
        <textarea
          ref={ref}
          value={text}
          placeholder={busy ? '正在生成…' : placeholderFor(mode, attachments.length)}
          onChange={e => { setText(e.target.value); autoSize(e.target) }}
          onKeyDown={onKey}
          onPaste={e => {
            for (const item of Array.from(e.clipboardData.items)) {
              if (item.type.startsWith('image/') || item.type.startsWith('text/')) {
                const f = item.getAsFile(); if (f) pickFile(f)
              }
            }
          }}
        />
      </div>

      {attachments.length > 0 && <div className="attachment-strip">
        {attachments.map((file, idx) => (
          <button key={`${file.name}-${idx}`} className="attachment-chip" type="button" onClick={() => setAttachments(p => p.filter((_, j) => j !== idx))} title="点击移除">
            {file.mime.startsWith('image/') ? <img src={file.url} alt="" /> : <span className="file-badge">{extensionFor(file.name)}</span>}
            <span>{file.name}</span>
            <em>×</em>
          </button>
        ))}
      </div>}

      <div className="composer-toolbar">
        <button className="tool-round" type="button" onClick={() => setPanel(panel === 'add' ? null : 'add')} aria-label="添加附件">+</button>
        {activeExpert && <span className="expert-chip" title={activeExpert.profession}>
          {activeExpert.avatarUrl
            ? <img src={activeExpert.avatarUrl} alt="" onError={e => { (e.currentTarget as HTMLImageElement).style.display = 'none' }} />
            : <b>{activeExpert.name.slice(0, 1)}</b>}
          <span>{activeExpert.name}</span>
          <em onClick={e => { e.stopPropagation(); onClearExpert?.() }} title="取消召唤">×</em>
        </span>}
        <button className="tool-chip workspace-chip" type="button" onClick={() => setPanel(panel === 'workspace' ? null : 'workspace')}>
          <span className="chip-icon" aria-hidden="true">□</span>
          <strong>选择工作空间</strong>
        </button>
        <button
          className={`tool-chip permission-chip ${permission === 'full' ? 'danger' : ''}`}
          type="button"
          onClick={() => setPanel(panel === 'permission' ? null : 'permission')}>
          <span className="chip-icon" aria-hidden="true">!</span>
          <strong>{permissionLabel}</strong>
        </button>
        <span className="toolbar-spacer" />
        <button className="tool-chip" type="button" onClick={() => setPanel(panel === 'expert' ? null : 'expert')}>{selectedAgent ? selectedAgentName : '专家'}</button>
        <button className="tool-chip" type="button" onClick={() => setPanel(panel === 'skill' ? null : 'skill')}>{selectedSkills.length > 0 ? `技能 ${selectedSkills.length}` : '技能'}</button>
        <button className="model-chip" type="button" onClick={() => setPanel(panel === 'model' ? null : 'model')}>{modelLabel}</button>
        <button className="mic-btn" type="button" aria-label="语音输入">⌕</button>
        {busy
          ? <button className="send advanced" onClick={onAbort}>中止</button>
          : <button className="send advanced" onClick={submit} disabled={!canSubmit(text, attachments.length, mode) || parsingAttachments}><span>{parsingAttachments ? '解析中' : mode === 'chat' ? '发送' : '生成'}</span></button>
        }
      </div>

      <input ref={fileRef} type="file" multiple accept="image/*,.pdf,.txt,.md,.json,.csv" style={{ display: 'none' }}
        onChange={e => {
          Array.from(e.target.files ?? []).forEach(pickFile)
          e.currentTarget.value = ''
        }} />

      {panel && <div className={`composer-popover ${panel}-panel`}>
        {panel === 'add' && <>
          <button className="popover-row" type="button" onClick={openFiles}><span>◎</span><strong>添加文件</strong><em>›</em></button>
          <button className="popover-row" type="button" onClick={() => setPanel('mode')}><span>✦</span><strong>模式</strong><em>›</em></button>
          <button className="popover-row" type="button" onClick={() => setPanel('expert')}><span>☷</span><strong>专家</strong><em>›</em></button>
          <button className="popover-row active" type="button" onClick={() => setPanel('skill')}><span>⌁</span><strong>技能</strong><em>›</em></button>
          <button className="popover-row" type="button"><span>∞</span><strong>连接器</strong><em>›</em></button>
        </>}

        {panel === 'mode' && <>
          <PanelTitle title="模式" />
          <ChoiceButton label="聊天" active={mode === 'chat'} onClick={() => { setMode('chat'); setPanel(null) }} />
          <ChoiceButton label="文生图" active={mode === 'text-to-image'} onClick={() => { setMode('text-to-image'); setPanel(null) }} />
          <ChoiceButton label="图生图" active={mode === 'image-to-image'} onClick={() => { setMode('image-to-image'); setPanel(null) }} />
        </>}

        {panel === 'workspace' && <>
          <div className="workspace-picker-head">
            <label className="workspace-search" htmlFor="workspace-search-input">
              <span aria-hidden="true">⌕</span>
              <input
                id="workspace-search-input"
                value={workspaceSearch}
                onChange={e => setWorkspaceSearch(e.target.value)}
                placeholder="搜索工作空间"
              />
            </label>
          </div>

          <button className="workspace-row" type="button" onClick={() => chooseWorkspace(workspace || workspaceRoots[0]?.id || '')}>
            <span className="workspace-row-icon" aria-hidden="true">□</span>
            <span className="workspace-row-text" title={workspaceLabel}>{workspaceLabel}</span>
          </button>

          <div className="workspace-divider" />

          <button className="workspace-row action" type="button" onClick={chooseLocalWorkspace}>
            <span className="workspace-row-icon" aria-hidden="true">+</span>
            <span className="workspace-row-text">新建工作空间</span>
          </button>

          <button className="workspace-row action" type="button" onClick={chooseLocalWorkspace}>
            <span className="workspace-row-icon" aria-hidden="true">□</span>
            <span className="workspace-row-text">打开本地文件夹</span>
          </button>
        </>}

        {panel === 'permission' && <>
          <div className="permission-panel-body">
            <p>当前权限为允许完全访问，请注意数据安全，建议执行可信任的任务。</p>
            <div className="permission-divider" />
            <label className="permission-toggle-row">
              <span>允许完全访问</span>
              <input
                type="checkbox"
                checked={permission === 'full'}
                onChange={e => choosePermission(e.target.checked ? 'full' : 'confirm')}
              />
            </label>
          </div>
        </>}

        {panel === 'model' && <>
          <PanelTitle title="模型选择" hint="云端服务 · 默认 Auto" />
          <ChoiceButton label="Auto" detail="由云端服务和 Agent 配置自动选择" active={!selectedProvider && !selectedModel} onClick={() => setSelectedModel(null, null)} />
          {modelOptions.map(item => <ChoiceButton key={`${item.providerId}-${item.modelName}`} label={item.modelName} detail={item.providerId} active={selectedProvider === item.providerId && selectedModel === item.modelName} onClick={() => setSelectedModel(item.providerId, item.modelName)} />)}
        </>}

        {panel === 'expert' && <>
          <PanelTitle title="使用专家" />
          <ChoiceButton label="默认助理" detail="不指定专家" active={!selectedAgent} onClick={() => { onAgentChange?.(null); setPanel(null) }} />
          {agents.map(agent => <ChoiceButton key={agent.id} label={agent.name} detail={agent.description} active={selectedAgent === agent.id} onClick={() => { onAgentChange?.(agent.id); setPanel(null) }} />)}
          {agents.length === 0 && <div className="popover-empty">暂无专家</div>}
        </>}

        {panel === 'skill' && <>
          <div className="skill-choice-list">
            {visibleSkills.map(skill => <button key={skill.id} type="button" className={`skill-choice ${selectedSkills.includes(skill.id) ? 'selected' : ''}`} onClick={() => toggleSkill(skill.id)}>
              <span>{skill.name.slice(0, 1).toUpperCase()}</span>
              <strong>{skill.name}</strong>
              <em>{skill.description}</em>
              <small>{skill.id.startsWith('agent.') ? '.agents/skills' : skill.requiresFilesystem ? '本地 Skill.md' : '内置能力'}</small>
            </button>)}
            {visibleSkills.length === 0 && <div className="popover-empty">没有匹配的技能</div>}
          </div>
          <div className="popover-footer">
            <button type="button" onClick={openFiles}>从本地添加技能</button>
            <button type="button" onClick={onOpenSkillManager}>管理技能</button>
          </div>
        </>}
      </div>}
    </div>
  )
}

function PanelTitle({ title, hint }: { title: string; hint?: string }) {
  return <div className="popover-title"><strong>{title}</strong>{hint && <span>{hint}</span>}</div>
}

function ChoiceButton({ label, detail, active, onClick }: { label: string; detail?: string; active: boolean; onClick: () => void }) {
  return <button type="button" className={`choice-row ${active ? 'active' : ''}`} onClick={onClick}>
    <strong>{label}</strong>
    {detail && <span>{detail}</span>}
  </button>
}

function placeholderFor(mode: ComposerMode, attachmentCount: number): string {
  if (attachmentCount > 0 && mode === 'chat') return '说说你想如何处理这些附件'
  if (mode === 'text-to-image') return '描述要生成的图片,Enter 生成 · Shift+Enter 换行'
  if (mode === 'image-to-image') return '上传参考图并描述改造方向,Enter 生成 · Shift+Enter 换行'
  return '今天帮你做些什么？ @ 引用对话文件，/ 调用技能与指令'
}

function canSubmit(text: string, imageCount: number, mode: ComposerMode): boolean {
  const hasText = text.trim().length > 0
  if (mode === 'chat') return hasText || imageCount > 0
  if (mode === 'text-to-image') return hasText
  return hasText && imageCount > 0
}

function extensionFor(name: string): string {
  const parts = name.split('.')
  return parts.length > 1 ? parts[parts.length - 1].slice(0, 4).toUpperCase() : 'FILE'
}

function appendAttachmentSummary(text: string, files: Attachment[]): string {
  if (files.length === 0) return text
  const summary = files.map(file => `- ${file.name} (${formatBytes(file.size)}, ${file.mime || 'application/octet-stream'})`).join('\n')
  return `${text}\n\n附件：\n${summary}`.trim()
}

async function appendAttachmentAnalysis(text: string, files: Attachment[]): Promise<string> {
  if (files.length === 0) return text

  const parsedChunks: string[] = []
  for (const file of files) {
    if (!file.file) continue
    const formData = new FormData()
    formData.append('file', file.file)
    formData.append('title', file.name)
    const result = await parseKnowledgeFile(formData)
    const content = (result.documents?.[0]?.content ?? '').trim()
    if (!content) continue
    const snippet = content.length > 6000 ? `${content.slice(0, 6000)}\n...(已截断)` : content
    parsedChunks.push(`【附件 ${file.name} 解析内容】\n${snippet}`)
  }

  if (parsedChunks.length === 0) return appendAttachmentSummary(text, files)
  return `${appendAttachmentSummary(text, files)}\n\n请结合以下附件内容进行解读分析：\n\n${parsedChunks.join('\n\n')}`.trim()
}

function formatBytes(size: number): string {
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${Math.round(size / 1024)} KB`
  return `${(size / 1024 / 1024).toFixed(1)} MB`
}
