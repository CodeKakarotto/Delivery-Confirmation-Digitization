import { useEffect, useState } from 'react'
import { getCurrentUser, logout, type User } from './api'
import { ApiStatus } from './ApiStatus'
import { HistoryPage } from './HistoryPage'
import { HomePage } from './HomePage'
import { LoginPage } from './LoginPage'
import { ReviewPage } from './ReviewPage'
import { ReviewQueuePage } from './ReviewQueuePage'
import { UploadPage } from './UploadPage'
import './App.css'

type Page = 'home' | 'upload' | 'history' | 'review-queue' | 'review'

function App() {
  // undefined = still checking whether someone is already signed in
  const [user, setUser] = useState<User | null | undefined>(undefined)
  const [page, setPage] = useState<Page>('home')
  const [reviewing, setReviewing] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const openQueue = (message: string | null = null) => {
    setNotice(message)
    setReviewing(null)
    setPage('review-queue')
  }

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
    setPage('home')
  }

  return (
    <main className="app">
      <h1>Michael's Flowers – Delivery Confirmations</h1>

      {user === undefined && <p>Loading…</p>}
      {user === null && <LoginPage onSignedIn={setUser} />}
      {user && (
        <>
          <div className="signed-in">
            <span>
              Signed in as {user.email} ({user.role})
            </span>
            <button type="button" onClick={handleSignOut}>
              Sign out
            </button>
          </div>
          {page === 'home' && (
            <HomePage
              onOpenUpload={() => setPage('upload')}
              onOpenReview={() => openQueue()}
              onOpenHistory={() => setPage('history')}
            />
          )}
          {page === 'upload' && <UploadPage onBack={() => setPage('home')} />}
          {page === 'history' && <HistoryPage onBack={() => setPage('home')} />}
          {page === 'review-queue' && (
            <ReviewQueuePage
              notice={notice}
              onBack={() => setPage('home')}
              onOpen={(id) => {
                setReviewing(id)
                setPage('review')
              }}
            />
          )}
          {page === 'review' && reviewing && (
            <ReviewPage
              key={reviewing}
              confirmationId={reviewing}
              onBack={() => openQueue()}
              onDone={(message) => openQueue(message)}
            />
          )}
        </>
      )}

      <footer>
        <ApiStatus />
      </footer>
    </main>
  )
}

export default App
