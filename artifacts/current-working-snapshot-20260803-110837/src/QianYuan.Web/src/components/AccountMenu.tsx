import { useEffect, useState } from 'react'
import type { AuthUserDto, CreditWalletDto } from '../types/api'
import { getCreditWallet } from '../services/api'

interface Props {
  user: AuthUserDto
  theme: 'light' | 'dark'
  placement: 'topbar' | 'sidebar'
  onThemeChange: (theme: 'light' | 'dark') => void
  onOpenCredits: () => void
  onOpenGrowthPlan: () => void
  onOpenSettings: () => void
  onOpenHelp: () => void
  onCheckUpdates: () => void
  onSignOut: () => void
  onClose: () => void
}

export function AccountMenu({
  user, theme, placement, onThemeChange, onOpenCredits, onOpenGrowthPlan, onOpenSettings, onOpenHelp, onCheckUpdates, onSignOut, onClose,
}: Props) {
  const [wallet, setWallet] = useState<CreditWalletDto | null>(null)

  useEffect(() => {
    let alive = true
    getCreditWallet()
      .then(result => { if (alive) setWallet(result) })
      .catch(() => undefined)
    return () => { alive = false }
  }, [])

  const balanceText = wallet ? wallet.balance.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '--'

  return (
    <div className="account-menu-layer" onClick={onClose}>
      <div className={`account-menu-card from-${placement}`} onClick={event => event.stopPropagation()} role="dialog" aria-label="用户菜单">
        <button className="account-menu-row featured" type="button" onClick={onOpenCredits}>
          <span className="account-menu-icon sparkle" aria-hidden="true" />
          <span>积分余额</span>
          <strong>{balanceText}</strong>
          <i aria-hidden="true">›</i>
        </button>
        <button className="account-menu-row" type="button" onClick={onOpenGrowthPlan}>
          <span className="account-menu-icon calendar" aria-hidden="true" />
          <span>成长计划</span>
          <strong>连登抽取 Buddy 周边</strong>
          <i aria-hidden="true">›</i>
        </button>

        <div className="account-menu-divider" />

        <button className="account-menu-row" type="button" onClick={onOpenSettings}>
          <span className="account-menu-icon hex" aria-hidden="true" />
          <span>设置</span>
        </button>
        <div className="account-menu-row appearance-row">
          <span className="account-menu-icon palette" aria-hidden="true" />
          <span>外观</span>
          <div className="appearance-switch" aria-label="外观模式">
            <button className={theme === 'light' ? 'active' : ''} type="button" onClick={() => onThemeChange('light')}>浅色</button>
            <button className={theme === 'dark' ? 'active' : ''} type="button" onClick={() => onThemeChange('dark')}>深色</button>
          </div>
        </div>
        <button className="account-menu-row" type="button" onClick={onOpenHelp}>
          <span className="account-menu-icon help" aria-hidden="true" />
          <span>帮助与反馈</span>
        </button>
        <button className="account-menu-row" type="button" onClick={onCheckUpdates}>
          <span className="account-menu-icon update" aria-hidden="true" />
          <span>检查更新</span>
        </button>

        <div className="account-menu-divider" />

        <button className="account-menu-row" type="button" onClick={onSignOut}>
          <span className="account-menu-icon logout" aria-hidden="true" />
          <span>退出登录</span>
        </button>

        <div className="account-menu-user" title={user.email}>{user.displayName} · {user.status}</div>
      </div>
    </div>
  )
}