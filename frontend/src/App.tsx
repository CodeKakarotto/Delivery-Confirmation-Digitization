import { useEffect, useState } from 'react'
import './App.css'

// Empty in local development (Vite proxies /api to the backend).
// Set to the Cloudflare Tunnel API address in production (Step 10).
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? ''

type ApiStatus = 'checking' | 'OK' | 'unavailable'

function App() {
  const [apiStatus, setApiStatus] = useState<ApiStatus>('checking')

  useEffect(() => {
    let cancelled = false

    async function checkHealth() {
      try {
        const response = await fetch(`${API_BASE_URL}/api/health`)
        const body = response.ok ? await response.json() : null
        if (!cancelled) {
          setApiStatus(body?.status === 'OK' ? 'OK' : 'unavailable')
        }
      } catch {
        if (!cancelled) setApiStatus('unavailable')
      }
    }

    checkHealth()
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <main className="app">
      <h1>Michael's Flowers – Delivery Confirmations</h1>
      <p className={`status status-${apiStatus.toLowerCase()}`}>
        API status: {apiStatus}
      </p>
    </main>
  )
}

export default App
