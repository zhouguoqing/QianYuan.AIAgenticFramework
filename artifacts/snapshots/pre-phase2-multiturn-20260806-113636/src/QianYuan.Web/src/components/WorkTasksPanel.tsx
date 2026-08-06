import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import type {
  ExpertTeamDto,
  ExpertTeamExecutionEventDto,
  ExpertTeamMemberDto,
  ExpertTeamTemplateDto,
  WorkTaskDetailDto,
  WorkTaskDto,
  WorkTaskRuntimeDto,
} from '../types/api'
import {
  addExpertTeamMember,
  cancelWorkTask,
  createExpertTeamFromTemplate,
  createWorkTask,
  deleteExpertTeam,
  deleteExpertTeamMember,
  executeWorkTaskStream,
  getWorkTask,
  getWorkTaskRuntime,
  listExpertTeamTemplates,
  listExpertTeams,
  listWorkTasks,
  orchestrateWorkTask,
  updateExpertTeam,
  updateExpertTeamMember,
} from '../services/api'

type Props = { provider?: string; model?: string; onClose: () => void }

type TeamDraft = { name: string; description: string; scenario: string }
type MemberDraft = {
  memberOrder: string
  roleId: string
  displayName: string
  agentId: string
  responsibility: string
  executionMode: 'Sequential' | 'Parallel'
  enabled: boolean
}

const emptyTeamDraft: TeamDraft = { name: '', description: '', scenario: 'custom' }
const emptyMemberDraft: MemberDraft = {
  memberOrder: '',
  roleId: 'expert',
  displayName: '',
  agentId: '',
  responsibility: '',
  executionMode: 'Sequential',
  enabled: true,
}

export function WorkTasksPanel({ provider, model, onClose }: Props) {
  const [tasks, setTasks] = useState<WorkTaskDto[]>([])
  const [teams, setTeams] = useState<ExpertTeamDto[]>([])
  const [templates, setTemplates] = useState<ExpertTeamTemplateDto[]>([])
  const [selected, setSelected] = useState<WorkTaskDetailDto | null>(null)
  const [runtime, setRuntime] = useState<WorkTaskRuntimeDto | null>(null)
  const [title, setTitle] = useState('')
  const [goal, setGoal] = useState('')
  const [teamId, setTeamId] = useState('')
  const [templateId, setTemplateId] = useState('')
  const [manageTeamId, setManageTeamId] = useState('')
  const [teamDraft, setTeamDraft] = useState<TeamDraft>(emptyTeamDraft)
  const [memberDraft, setMemberDraft] = useState<MemberDraft>(emptyMemberDraft)
  const [editingMemberId, setEditingMemberId] = useState<string | null>(null)
  const [events, setEvents] = useState<ExpertTeamExecutionEventDto[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const streamAbortRef = useRef<AbortController | null>(null)

  const managedTeam = useMemo(() => teams.find(t => t.id === manageTeamId) ?? null, [teams, manageTeamId])
  const selectedTaskTeam = useMemo(() => teams.find(t => t.id === selected?.task.teamId) ?? null, [teams, selected?.task.teamId])

  useEffect(() => {
    void loadInitial()
    return () => streamAbortRef.current?.abort()
  }, [])

  useEffect(() => {
    const firstTeam = teams[0]?.id ?? ''
    setTeamId(prev => prev || firstTeam)
    setManageTeamId(prev => prev || firstTeam)
  }, [teams])

  useEffect(() => {
    const firstTemplate = templates[0]?.id ?? ''
    setTemplateId(prev => prev || firstTemplate)
  }, [templates])

  useEffect(() => {
    if (!managedTeam) {
      setTeamDraft(emptyTeamDraft)
      return
    }
    setTeamDraft({ name: managedTeam.name, description: managedTeam.description, scenario: managedTeam.scenario })
    setEditingMemberId(null)
    setMemberDraft(emptyMemberDraft)
  }, [managedTeam])

  useEffect(() => {
    if (!selected?.task.id || !runtime?.isRunning) return
    const timer = window.setInterval(async () => {
      try {
        const next = await getWorkTaskRuntime(selected.task.id)
        setRuntime(next)
        if (!next.isRunning) await refreshTask(selected.task.id)
      } catch {
        // Keep the current runtime if polling temporarily fails.
      }
    }, 2000)
    return () => window.clearInterval(timer)
  }, [selected?.task.id, runtime?.isRunning])

  async function loadInitial() {
    try {
      const [teamItems, templateItems, taskItems] = await Promise.all([listExpertTeams(), listExpertTeamTemplates(), listWorkTasks()])
      setTeams(teamItems)
      setTemplates(templateItems)
      setTasks(taskItems)
    } catch (err) {
      setError(toMessage(err, 'Failed to load workbench data'))
    }
  }

  async function refreshTeams(preferredId?: string) {
    const teamItems = await listExpertTeams()
    setTeams(teamItems)
    const nextId = preferredId && teamItems.some(t => t.id === preferredId) ? preferredId : teamItems[0]?.id ?? ''
    setManageTeamId(nextId)
    setTeamId(prev => (prev && teamItems.some(t => t.id === prev)) ? prev : nextId)
  }

  async function refreshTasks() {
    setTasks(await listWorkTasks())
  }

  async function refreshTask(id: string) {
    const detail = await getWorkTask(id)
    setSelected(detail)
    setRuntime(await safeRuntime(id))
    await refreshTasks()
  }

  async function safeRuntime(id: string) {
    try {
      return await getWorkTaskRuntime(id)
    } catch {
      return null
    }
  }

  async function openTask(id: string) {
    setError(null)
    await refreshTask(id)
  }

  async function submitTask(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const created = await createWorkTask({ title, goal, teamId: teamId || undefined, providerId: provider || undefined, model: model || undefined })
      setSelected(created)
      setTitle('')
      setGoal('')
      await refreshTasks()
    } catch (err) {
      setError(toMessage(err, 'Failed to create task'))
    } finally {
      setBusy(false)
    }
  }

  async function createTeamFromTemplate() {
    if (!templateId) return
    setBusy(true)
    setError(null)
    try {
      const team = await createExpertTeamFromTemplate(templateId)
      await refreshTeams(team.id)
    } catch (err) {
      setError(toMessage(err, 'Failed to create team from template'))
    } finally {
      setBusy(false)
    }
  }

  async function saveTeam() {
    if (!managedTeam) return
    setBusy(true)
    setError(null)
    try {
      const updated = await updateExpertTeam(managedTeam.id, { ...teamDraft, enabled: true })
      await refreshTeams(updated.id)
    } catch (err) {
      setError(toMessage(err, 'Failed to update team'))
    } finally {
      setBusy(false)
    }
  }

  async function removeTeam() {
    if (!managedTeam || !window.confirm(`Delete team ${managedTeam.name}?`)) return
    setBusy(true)
    setError(null)
    try {
      await deleteExpertTeam(managedTeam.id)
      await refreshTeams()
    } catch (err) {
      setError(toMessage(err, 'Failed to delete team'))
    } finally {
      setBusy(false)
    }
  }

  function editMember(member: ExpertTeamMemberDto) {
    setEditingMemberId(member.id)
    setMemberDraft({
      memberOrder: String(member.memberOrder),
      roleId: member.roleId,
      displayName: member.displayName,
      agentId: member.agentId,
      responsibility: member.responsibility,
      executionMode: member.executionMode === 'Parallel' ? 'Parallel' : 'Sequential',
      enabled: member.enabled,
    })
  }

  async function saveMember(event: FormEvent) {
    event.preventDefault()
    if (!managedTeam) return
    setBusy(true)
    setError(null)
    try {
      const payload = {
        roleId: memberDraft.roleId,
        displayName: memberDraft.displayName,
        agentId: memberDraft.agentId || null,
        responsibility: memberDraft.responsibility,
        executionMode: memberDraft.executionMode,
      }
      if (editingMemberId) {
        await updateExpertTeamMember(managedTeam.id, editingMemberId, {
          ...payload,
          memberOrder: memberDraft.memberOrder ? Number(memberDraft.memberOrder) : null,
          enabled: memberDraft.enabled,
        })
      } else {
        await addExpertTeamMember(managedTeam.id, payload)
      }
      await refreshTeams(managedTeam.id)
      setEditingMemberId(null)
      setMemberDraft(emptyMemberDraft)
    } catch (err) {
      setError(toMessage(err, 'Failed to save member'))
    } finally {
      setBusy(false)
    }
  }

  async function removeMember(memberId: string) {
    if (!managedTeam || !window.confirm('Delete this member?')) return
    setBusy(true)
    setError(null)
    try {
      await deleteExpertTeamMember(managedTeam.id, memberId)
      await refreshTeams(managedTeam.id)
    } catch (err) {
      setError(toMessage(err, 'Failed to delete member'))
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
      await refreshTasks()
    } catch (err) {
      setError(toMessage(err, 'Failed to orchestrate task'))
    } finally {
      setBusy(false)
    }
  }

  async function runStream() {
    if (!selected) return
    streamAbortRef.current?.abort()
    const abort = new AbortController()
    streamAbortRef.current = abort
    setBusy(true)
    setError(null)
    setEvents([])
    try {
      for await (const evt of executeWorkTaskStream(selected.task.id, selected.task.teamId || teamId || undefined, 8, 180, abort.signal)) {
        setEvents(prev => [evt, ...prev].slice(0, 80))
      }
      await refreshTask(selected.task.id)
    } catch (err) {
      if (!abort.signal.aborted) setError(toMessage(err, 'Failed to execute task'))
      await refreshTask(selected.task.id).catch(() => undefined)
    } finally {
      setBusy(false)
      streamAbortRef.current = null
    }
  }

  async function cancel() {
    if (!selected) return
    streamAbortRef.current?.abort()
    setBusy(true)
    setError(null)
    try {
      const next = await cancelWorkTask(selected.task.id, 'cancel from work tasks panel')
      setRuntime(next)
      await refreshTask(selected.task.id)
    } catch (err) {
      setError(toMessage(err, 'Failed to cancel task'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop">
      <div className="modal worktask-modal">
        <div className="modal-header">
          <div className="workbench-title">
            <strong>QIANYUAN Expert Team Workbench</strong>
            <span>Build teams, orchestrate work, and stream execution progress.</span>
          </div>
          <span style={{ flex: 1 }} />
          <button className="ghost" onClick={onClose}>Close</button>
        </div>
        <div className="worktask-body">
          <aside className="worktask-list-pane">
            <form className="worktask-form" onSubmit={submitTask}>
              <label>
                <span>Title</span>
                <input value={title} onChange={e => setTitle(e.target.value)} placeholder="Competitive analysis report" />
              </label>
              <label>
                <span>Goal</span>
                <textarea value={goal} onChange={e => setGoal(e.target.value)} rows={5} required placeholder="Describe the deliverable you expect from the expert team." />
              </label>
              <label>
                <span>Expert team</span>
                <select value={teamId} onChange={e => setTeamId(e.target.value)}>
                  {teams.map(team => <option key={team.id} value={team.id}>{team.name}</option>)}
                </select>
              </label>
              <button className="primary-inline-btn" disabled={busy}>{busy ? 'Working...' : '+ Create task'}</button>
            </form>
            {error && <div className="alert-error compact">{error}</div>}
            <div className="worktask-list">
              {tasks.length === 0 && <div className="muted-small">No tasks yet</div>}
              {tasks.map(task => <button key={task.id} className={`worktask-row ${selected?.task.id === task.id ? 'active' : ''}`} onClick={() => openTask(task.id)}>
                <strong>{task.title}</strong>
                <span>{task.status} · {task.stepCount} steps · {task.artifactCount} artifacts</span>
              </button>)}
            </div>
          </aside>

          <main className="worktask-detail-pane">
            <section className="team-admin-card">
              <div className="team-admin-head">
                <h3>Expert Team Templates</h3>
                <div className="team-admin-actions">
                  <select value={templateId} onChange={e => setTemplateId(e.target.value)}>
                    {templates.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                  </select>
                  <button className="primary-inline-btn" disabled={busy || !templateId} onClick={createTeamFromTemplate}>Create from template</button>
                </div>
              </div>
              {templateId && <p className="muted-small">{templates.find(t => t.id === templateId)?.description}</p>}
            </section>

            <section className="team-admin-card">
              <div className="team-admin-head">
                <h3>Team Editor</h3>
                <select value={manageTeamId} onChange={e => setManageTeamId(e.target.value)}>
                  {teams.map(team => <option key={team.id} value={team.id}>{team.name}</option>)}
                </select>
              </div>
              {managedTeam ? <>
                <div className="team-form-grid">
                  <label><span>Name</span><input value={teamDraft.name} onChange={e => setTeamDraft({ ...teamDraft, name: e.target.value })} /></label>
                  <label><span>Scenario</span><input value={teamDraft.scenario} onChange={e => setTeamDraft({ ...teamDraft, scenario: e.target.value })} /></label>
                  <label className="wide"><span>Description</span><textarea rows={2} value={teamDraft.description} onChange={e => setTeamDraft({ ...teamDraft, description: e.target.value })} /></label>
                </div>
                <div className="team-admin-actions right">
                  <button className="primary-inline-btn" disabled={busy || !teamDraft.name.trim()} onClick={saveTeam}>Save team</button>
                  <button className="ghost" disabled={busy} onClick={removeTeam}>Delete team</button>
                </div>
                <div className="team-member-list">
                  {managedTeam.members.map(member => <div className="team-member-row" key={member.id}>
                    <div>
                      <strong>{member.memberOrder}. {member.displayName}</strong>
                      <span>{member.roleId} · {member.executionMode} · {member.enabled ? 'Enabled' : 'Disabled'}</span>
                      <p>{member.responsibility}</p>
                    </div>
                    <button className="ghost" onClick={() => editMember(member)}>Edit</button>
                    <button className="ghost" onClick={() => removeMember(member.id)}>Delete</button>
                  </div>)}
                </div>
                <form className="member-form" onSubmit={saveMember}>
                  <h4>{editingMemberId ? 'Edit member' : 'Add member'}</h4>
                  <div className="team-form-grid">
                    <label><span>Order</span><input value={memberDraft.memberOrder} onChange={e => setMemberDraft({ ...memberDraft, memberOrder: e.target.value })} placeholder="Auto" /></label>
                    <label><span>Role ID</span><input value={memberDraft.roleId} onChange={e => setMemberDraft({ ...memberDraft, roleId: e.target.value })} required /></label>
                    <label><span>Name</span><input value={memberDraft.displayName} onChange={e => setMemberDraft({ ...memberDraft, displayName: e.target.value })} required /></label>
                    <label><span>Agent ID</span><input value={memberDraft.agentId} onChange={e => setMemberDraft({ ...memberDraft, agentId: e.target.value })} placeholder="Default agent" /></label>
                    <label><span>Mode</span><select value={memberDraft.executionMode} onChange={e => setMemberDraft({ ...memberDraft, executionMode: e.target.value as 'Sequential' | 'Parallel' })}><option value="Sequential">Sequential</option><option value="Parallel">Parallel</option></select></label>
                    <label><span>Enabled</span><select value={String(memberDraft.enabled)} onChange={e => setMemberDraft({ ...memberDraft, enabled: e.target.value === 'true' })}><option value="true">Enabled</option><option value="false">Disabled</option></select></label>
                    <label className="wide"><span>Responsibility</span><textarea rows={2} value={memberDraft.responsibility} onChange={e => setMemberDraft({ ...memberDraft, responsibility: e.target.value })} required /></label>
                  </div>
                  <div className="team-admin-actions right">
                    {editingMemberId && <button type="button" className="ghost" onClick={() => { setEditingMemberId(null); setMemberDraft(emptyMemberDraft) }}>Cancel edit</button>}
                    <button className="primary-inline-btn" disabled={busy}>Save member</button>
                  </div>
                </form>
              </> : <div className="muted-small">Create a team from a template to start editing.</div>}
            </section>

            {selected ? <section className="team-admin-card task-detail-card">
              <div className="worktask-detail-head">
                <div>
                  <h2>{selected.task.title}</h2>
                  <p>{selected.task.goal}</p>
                </div>
                <div className="worktask-actions">
                  <span className="mini-status">{selected.task.status}</span>
                  {runtime && <span className="mini-status">Runtime: {runtime.status}</span>}
                  <button className="primary-inline-btn" disabled={busy} onClick={orchestrate}>Orchestrate</button>
                  <button className="primary-inline-btn" disabled={busy} onClick={runStream}>Run stream</button>
                  <button className="ghost" disabled={!busy && !runtime?.isRunning} onClick={cancel}>Cancel</button>
                </div>
              </div>
              <div className="worktask-team-note">Bound team: {selectedTaskTeam?.name ?? selected.task.teamId ?? 'Not bound'}</div>
              {events.length > 0 && <div className="execution-log">
                <h3>Execution stream</h3>
                {events.map((evt, idx) => <div className="execution-event" key={`${evt.at}-${idx}`}>
                  <strong>{evt.stepOrder ? `${evt.stepOrder}. ${evt.stepName}` : evt.type}</strong>
                  <span>{evt.status} · {evt.executionMode ?? 'Task'} · {new Date(evt.at).toLocaleTimeString()}</span>
                  {evt.message && <p>{evt.message}</p>}
                </div>)}
              </div>}
              <div className="worktask-columns">
                <section>
                  <h3>Steps</h3>
                  {selected.steps.length === 0 && <div className="muted-small">No steps yet</div>}
                  {selected.steps.map(step => <div className="worktask-step" key={step.id}>
                    <strong>{step.stepOrder}. {step.name}</strong>
                    <span>{step.status} · {step.executionMode}</span>
                    {step.summary && <p>{step.summary}</p>}
                  </div>)}
                </section>
                <section>
                  <h3>Artifacts</h3>
                  {selected.artifacts.length === 0 && <div className="muted-small">No artifacts yet</div>}
                  {selected.artifacts.map(artifact => <div className="artifact-card" key={artifact.id}>
                    <div className="artifact-head">
                      <strong>{artifact.name}</strong>
                      <span>{Math.max(1, Math.ceil(artifact.sizeBytes / 1024))} KB</span>
                    </div>
                    <pre>{artifact.content || artifact.filePath || '(empty)'}</pre>
                  </div>)}
                </section>
              </div>
            </section> : <div className="knowledge-empty"><strong>Select or create a task</strong><span>Task steps and artifacts appear here.</span></div>}
          </main>
        </div>
      </div>
    </div>
  )
}

function toMessage(err: unknown, fallback: string) {
  return err instanceof Error && err.message ? err.message : fallback
}
