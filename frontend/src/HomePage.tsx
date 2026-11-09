import type { User } from './api'

type Props = { user: User; onSignOut: () => void }

export function HomePage({ user, onSignOut }: Props) {
  return (
    <section className="card">
      <div className="signed-in">
        <span>
          Signed in as {user.email} ({user.role})
        </span>
        <button type="button" onClick={onSignOut}>
          Sign out
        </button>
      </div>
      <p>Welcome! The delivery confirmation screens will appear here.</p>
    </section>
  )
}
