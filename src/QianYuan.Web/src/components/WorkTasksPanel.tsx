import { FormEvent, useEffect, useState } from 'react'
import type { ExpertTeamDto, WorkTaskDetailDto, WorkTaskDto } from '../types/api'
import { createWorkTask, executeWorkTask, getWorkTask, listExpertTeams, listWorkTasks, orchestrateWorkTask } from '../services/api'

interface Props {
  provider?: string | null
  model?: string | null
  onClose: () => void
}

export function WorkTasksPanel({ provider, model, onClose }: Props) {
  const [tasks, setTasks] = useState<WorkTaskDto[]>([])
  const [teams, setTeams] = useState<ExpertTeamDto[]>([])
  const [selected, setSelected] = useState<WorkTaskDetailDto | null>(null)
  const [title, setTitle] = useState('')
  const [goal, setGoal] = useState('')
  const [teamId, setTeamId] = useState<string>('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    reload()
    listExpertTeams().then(items => {
      setTeams(items)
      if (items.length > 0) setTeamId(items[0].id)
    }).catch(err => setError(err instanceof Error ? err.message : 'Failed to load expert teams'))
  }, [])

  function reload() {
    listWorkTasks().then(items => {
      setTasks(items)
      if (items.length > 0 && !selected) void openTask(items[0].id)
    }).catch(err => setError(err instanceof Error ? err.message : 'Failed to load tasks'))
  }

  async function openTask(id: string) {
    const detail = await getWorkTask(id)
    setSelected(detail)
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const created = await createWorkTask({ title, goal, teamId: teamId || undefined, providerId: provider || undefined, model: model || undefined })
      setTitle('')
      setGoal('')
      setSelected(created)
      const items = await listWorkTasks()
      setTasks(items)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create task')
    } finally {
      setBusy(false)
    }
  }

  async function orchestrate() {
    if (!selected) return
    setBusy(true)
    setError(null)
    try {
      const detail = await orchestrateWorkTask(selected.task.id, selected.task.teamId || teamId || undefined)
      setSelected(detail)
      const items = await listWorkTasks()
      setTasks(items)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to orchestrate task')
    } finally {
      setBusy(false)
    }
  }

  async function execute() {
    if (!selected) return
    setBusy(true)
    setError(null)
    try {
      const detail = await executeWorkTask(selected.task.id, selected.task.teamId || teamId || undefined)
      setSelected(detail)
      const items = await listWorkTasks()
      setTasks(items)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to execute task')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop">
      <div className="modal worktask-modal">
        <div className="modal-header">
          <strong>任务工作台</strong>
          <span style={{ flex: 1 }} />
          <button className="ghost" onClick={onClose}>关闭</button>
        </div>
        <div className="worktask-body">
          <aside className="worktask-list-pane">
            <form className="worktask-form" onSubmit={submit}>
              <label>
                <span>标题</span>
                <input value={title} onChange={e => setTitle(e.target.value)} placeholder="例如：竞品调研报告" />
              </label>
              <label>
                <span>目标</span>
                <textarea value={goal} onChange={e => setGoal(e.target.value)} rows={5} required placeholder="描述你希望专家团交付什么结果" />
              </label>
              <label>
                <span>专家团</span>
                <select value={teamId} onChange={e => setTeamId(e.target.value)}>
                  {teams.map(team => <option key={team.id} value={team.id}>{team.name}</option>)}
                </select>
              </label>
              <button className="primary-inline-btn" disabled={busy}>{busy ? '创建中...' : '+ 创建任务'}</button>
            </form>
            {error && <div className="alert-error compact">{error}</div>}
            <div className="worktask-list">
              {tasks.length === 0 && <div className="muted-small">暂无任务</div>}
              {tasks.map(task => <button key={task.id} className={`worktask-row ${selected?.task.id === task.id ? 'active' : ''}`} onClick={() => openTask(task.id)}>
                <strong>{task.title}</strong>
                <span>{task.status} · {task.artifactCount} 个产物</span>
              </button>)}
            </div>
          </aside>
          <main className="worktask-detail-pane">
            {selected ? <>
              <div className="worktask-detail-head">
                <div>
                  <h2>{selected.task.title}</h2>
                  <p>{selected.task.goal}</p>
                </div>
                <div className="worktask-actions">
                  <span className="mini-status">{selected.task.status}</span>
                  <button className="primary-inline-btn" disabled={busy} onClick={orchestrate}>编排专家团</button>
                  <button className="primary-inline-btn" disabled={busy} onClick={execute}>执行专家团</button>
                </div>
              </div>
              {selected.task.teamId && <div className="worktask-team-note">已绑定专家团：{teams.find(t => t.id === selected.task.teamId)?.name ?? selected.task.teamId}</div>}
              <div className="worktask-columns">
                <section>
                  <h3>步骤</h3>
                  {selected.steps.map(step => <div className="worktask-step" key={step.id}>
                    <strong>{step.stepOrder}. {step.name}</strong>
                    <span>{step.status}</span>
                    {step.summary && <p>{step.summary}</p>}
                  </div>)}
                </section>
                <section>
                  <h3>产物</h3>
                  {selected.artifacts.map(artifact => <div className="artifact-card" key={artifact.id}>
                    <div className="artifact-head">
                      <strong>{artifact.name}</strong>
                      <span>{Math.max(1, Math.ceil(artifact.sizeBytes / 1024))} KB</span>
                    </div>
                    <pre>{artifact.content || artifact.filePath || '(空)'}</pre>
                  </div>)}
                </section>
              </div>
            </> : <div className="knowledge-empty"><strong>选择或创建一个任务</strong><span>任务产物会显示在这里</span></div>}
          </main>
        </div>
      </div>
    </div>
  )
}