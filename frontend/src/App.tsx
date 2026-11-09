import { useEffect, useState } from 'react'
import { getCurrentUser, logout, type User } from './api'
import { ApiStatus } from './ApiStatus'
import { HomePage } from './HomePage'
import { LoginPage } from './LoginPage'
import './App.css'

function App() {
  // undefined = still checking whether someone is already signed in
  const [user, setUser] = useState<User | null | undefined>(undefined)

  useEffect(() => {
    let cancelled = false
    getCurrentUser().then((current) => {
      if (!cancelled) setUser(current)
    })
    return () => {
      cancelled = true
    }
  }, [])

  async function handleSignOut() {
    await logout()
    setUser(null)
  }

  return (
    <main className="app">
      <h1>Michael's Flowers – Delivery Confirmations</h1>

      {user === undefined && <p>Loading…</p>}
      {user === null && <LoginPage onSignedIn={setUser} />}
      {user && <HomePage user={user} onSignOut={handleSignOut} />}

      <footer>
        <ApiStatus />
      </footer>
    </main>
  )
}

export default App
