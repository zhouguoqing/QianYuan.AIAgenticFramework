import { FormEvent, useState } from 'react'
import type { AuthResponse } from '../types/api'
import { login, register } from '../services/api'

interface Props {
  onAuthenticated: (auth: AuthResponse) => void
}

export function AuthPage({ onAuthenticated }: Props) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
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
    <div className="auth-shell">
      <div className="auth-panel">
        <div className="auth-brand">WorkPartner</div>
        <h1>{mode === 'login' ? '登录工作台' : '创建账号'}</h1>
        <p>登录后进入 AI 专家团工作台，后续将接入 Credits、专家团和桌面端。</p>
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
          <button className="primary-btn" disabled={busy}>{busy ? '处理中...' : mode === 'login' ? '登录' : '注册并进入'}</button>
        </form>
        <button className="auth-switch" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
          {mode === 'login' ? '还没有账号？立即注册' : '已有账号？返回登录'}
        </button>
      </div>
    </div>
  )
}