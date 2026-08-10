import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import { ToastProvider } from './components/Toast'
import './styles.css'
import './styles/tokens.css'
import './styles/messages.css'
import './styles/composer.css'
import './styles/home.css'
import './styles/toast.css'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ToastProvider>
      <App />
    </ToastProvider>
  </React.StrictMode>
)
