import { useEffect, useState } from 'react'
import { checkHealth } from './api'

type Status = 'checking' | 'OK' | 'unavailable'

/** Small footer line showing whether the backend is running. */
export function ApiStatus() {
  const [status, setStatus] = useState<Status>('checking')

  useEffect(() => {
    let cancelled = false
    checkHealth().then((healthy) => {
      if (!cancelled) setStatus(healthy ? 'OK' : 'unavailable')
    })
    return () => {
      cancelled = true
    }
  }, [])

  return <p className={`status status-${status.toLowerCase()}`}>API status: {status}</p>
}
