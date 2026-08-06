import { useEffect, useState } from 'react'
import type { CreditTransactionDto, CreditWalletDto, SubscriptionPlanDto } from '../types/api'
import { getCreditWallet, listCreditTransactions, listPlans } from '../services/api'

interface Props {
  onClose: () => void
}

export function CreditsPanel({ onClose }: Props) {
  const [wallet, setWallet] = useState<CreditWalletDto | null>(null)
  const [transactions, setTransactions] = useState<CreditTransactionDto[]>([])
  const [plans, setPlans] = useState<SubscriptionPlanDto[]>([])
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    Promise.all([getCreditWallet(), listCreditTransactions(20), listPlans()])
      .then(([walletResult, transactionResult, planResult]) => {
        if (!alive) return
        setWallet(walletResult)
        setTransactions(transactionResult)
        setPlans(planResult)
      })
      .catch(err => alive && setError(err instanceof Error ? err.message : 'Failed to load credits'))
    return () => { alive = false }
  }, [])

  return (
    <div className="modal-backdrop">
      <div className="modal credits-modal">
        <div className="modal-header">
          <strong>Credits 管理</strong>
          <span style={{ flex: 1 }} />
          <button className="ghost" onClick={onClose}>关闭</button>
        </div>
        <div className="modal-body credits-body">
          {error && <div className="auth-error">{error}</div>}
          {wallet ? <section className="credits-summary">
            <div>
              <div className="credits-label">当前余额</div>
              <div className="credits-balance">{wallet.balance.toLocaleString()}</div>
            </div>
            <div>
              <div className="credits-label">套餐</div>
              <div className="credits-value">{wallet.planName}</div>
            </div>
            <div>
              <div className="credits-label">月额度</div>
              <div className="credits-value">{wallet.monthlyQuota.toLocaleString()} / {wallet.quotaMonth}</div>
            </div>
          </section> : <div className="muted-small">正在加载 Credits...</div>}

          <h3>套餐</h3>
          <div className="plan-grid">
            {plans.map(plan => <div className="plan-card" key={plan.id}>
              <div className="plan-head">
                <strong>{plan.name}</strong>
                <span>{plan.priceMonthlyCents === 0 ? '免费' : `¥${plan.priceMonthlyCents / 100}/月`}</span>
              </div>
              <div className="plan-meta">{plan.monthlyCredits.toLocaleString()} credits/月</div>
              <div className="plan-meta">助理 {plan.maxAssistants} · 项目 {plan.maxProjects} · 自动任务 {plan.maxAutoTasks}</div>
              <div className="plan-meta">{plan.allowAllModels ? '全部模型可选' : 'Auto 模型调度'}</div>
            </div>)}
          </div>

          <h3>最近流水</h3>
          <div className="transaction-list">
            {transactions.length === 0 && <div className="muted-small">暂无流水</div>}
            {transactions.map(item => <div className="transaction-row" key={item.id}>
              <div>
                <div>{item.description || item.sourceType}</div>
                <div className="muted-small">{new Date(item.createdAt).toLocaleString()}</div>
              </div>
              <div className={item.amount >= 0 ? 'credit-positive' : 'credit-negative'}>
                {item.amount >= 0 ? '+' : ''}{item.amount.toLocaleString()}
              </div>
            </div>)}
          </div>
        </div>
      </div>
    </div>
  )
}