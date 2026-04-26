import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { ProfileProvider } from './domain/useProfile.tsx'

// 為什麼把 ProfileProvider 包在最外層：
// - profile 是「整個前端的事實來源」（標題、選單、規格線都靠它），
//   一旦在某個子元件 lazily wrap，會讓不同頁面拿到不同 profile，DEBUG 起來很折磨。
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ProfileProvider>
      <App />
    </ProfileProvider>
  </StrictMode>,
)
