import { createContext, useContext, useState, useCallback, type ReactNode } from 'react'

type ToastType = 'success' | 'error' | 'warning' | 'info'

interface Toast {
  id: number
  type: ToastType
  title: string
  message?: string
  exiting?: boolean
}

interface ToastContextValue {
  success: (title: string, message?: string) => void
  error: (title: string, message?: string) => void
  warning: (title: string, message?: string) => void
  info: (title: string, message?: string) => void
}

const ToastContext = createContext<ToastContextValue | null>(null)

let nextId = 0

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])

  const add = useCallback((type: ToastType, title: string, message?: string) => {
    const id = ++nextId
    setToasts(prev => [...prev, { id, type, title, message }])
    setTimeout(() => {
      setToasts(prev => prev.map(t => t.id === id ? { ...t, exiting: true } : t))
      setTimeout(() => {
        setToasts(prev => prev.filter(t => t.id !== id))
      }, 250)
    }, 3500)
  }, [])

  const dismiss = useCallback((id: number) => {
    setToasts(prev => prev.map(t => t.id === id ? { ...t, exiting: true } : t))
    setTimeout(() => {
      setToasts(prev => prev.filter(t => t.id !== id))
    }, 250)
  }, [])

  return (
    <ToastContext.Provider value={{
      success: (t, m) => add('success', t, m),
      error: (t, m) => add('error', t, m),
      warning: (t, m) => add('warning', t, m),
      info: (t, m) => add('info', t, m),
    }}>
      {children}
      <div className="toast-container">
        {toasts.map(toast => (
          <div key={toast.id} className={`toast-item ${toast.type} ${toast.exiting ? 'exiting' : ''}`}>
            <div className="toast-icon">
              {toast.type === 'success' ? '✓' : toast.type === 'error' ? '✕' : toast.type === 'warning' ? '!' : 'i'}
            </div>
            <div className="toast-body">
              <div className="toast-title">{toast.title}</div>
              {toast.message && <div className="toast-message">{toast.message}</div>}
            </div>
            <button className="toast-close" onClick={() => dismiss(toast.id)}>×</button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used within ToastProvider')
  return ctx
}
