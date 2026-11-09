import { useState, type FormEvent } from 'react'
import { login, type User } from './api'

type Props = { onSignedIn: (user: User) => void }

export function LoginPage({ onSignedIn }: Props) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    const result = await login(email, password)
    setBusy(false)
    if (result.ok) {
      onSignedIn(result.user)
    } else {
      setError(result.message)
      setPassword('')
    }
  }

  return (
    <form className="card login" onSubmit={handleSubmit}>
      <h2>Sign in</h2>

      <label htmlFor="email">Email</label>
      <input
        id="email"
        type="email"
        autoComplete="username"
        required
        value={email}
        onChange={(e) => setEmail(e.target.value)}
      />

      <label htmlFor="password">Password</label>
      <input
        id="password"
        type="password"
        autoComplete="current-password"
        required
        value={password}
        onChange={(e) => setPassword(e.target.value)}
      />

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      <button type="submit" className="primary" disabled={busy}>
        Sign in
      </button>
    </form>
  )
}
