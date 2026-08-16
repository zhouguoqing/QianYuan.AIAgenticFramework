import { FormEvent, useState } from 'react'
import type { AuthResponse } from '../types/api'
import { login, register } from '../services/api'

interface Props {
  onAuthenticated: (auth: AuthResponse) => void
  onCancel?: () => void
  reason?: string
  initialMode?: 'login' | 'register'
}

export function AuthPage({ onAuthenticated, onCancel, reason, initialMode = 'login' }: Props) {
  const [mode, setMode] = useState<'login' | 'register'>(initialMode)
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const auth = mode === 'login'
        ? await login({ email, password })
        : await register({ email, password, displayName: displayName || undefined })
      onAuthenticated(auth)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authentication failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={onCancel ? 'auth-shell auth-modal-shell' : 'auth-shell'}>
      <div className="auth-panel">
        {onCancel && <button className="auth-close" type="button" onClick={onCancel} aria-label="关闭">×</button>}
        <div className="auth-brand">QianYuan</div>
        <h1>{mode === 'login' ? '登录后继续' : '注册并开始使用'}</h1>
        {reason && <p>{reason}</p>}
        <div className="auth-benefits" aria-label="登录后能力">
          <span>AI 专家团工作台</span>
          <span>Credits 账户</span>
          <span>桌面端同步</span>
        </div>
        <form onSubmit={submit} className="auth-form">
          <label>
            <span>邮箱</span>
            <input value={email} onChange={e => setEmail(e.target.value)} type="email" required autoComplete="email" />
          </label>
          {mode === 'register' && <label>
            <span>显示名称</span>
            <input value={displayName} onChange={e => setDisplayName(e.target.value)} autoComplete="name" />
          </label>}
          <label>
            <span>密码</span>
            <input value={password} onChange={e => setPassword(e.target.value)} type="password" required minLength={8} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} />
          </label>
          {error && <div className="auth-error">{error}</div>}
          <button className="primary-btn" disabled={busy}>{busy ? '处理中...' : mode === 'login' ? '登录并进入工作台' : '注册并进入工作台'}</button>
        </form>
        <button className="auth-switch" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
          {mode === 'login' ? '还没有账号？立即注册' : '已有账号？返回登录'}
        </button>
      </div>
    </div>
  )
}