// All calls to the ASP.NET Core backend live here.
// Empty base URL in local development (Vite proxies /api to the backend);
// set VITE_API_BASE_URL to the Cloudflare Tunnel API address in Step 10.
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? ''

const NETWORK_ERROR = "Can't reach the server. Please try again in a minute."
const GENERIC_LOGIN_ERROR = 'Sign-in failed. Please try again.'

export type Role = 'Employee' | 'Manager'
export type User = { email: string; role: Role }
export type LoginResult = { ok: true; user: User } | { ok: false; message: string }

function isUser(value: unknown): value is User {
  if (typeof value !== 'object' || value === null) return false
  const v = value as Record<string, unknown>
  return typeof v.email === 'string' && (v.role === 'Employee' || v.role === 'Manager')
}

function messageFrom(body: unknown): string {
  if (typeof body === 'object' && body !== null) {
    const message = (body as Record<string, unknown>).message
    if (typeof message === 'string' && message.length > 0) return message
  }
  return GENERIC_LOGIN_ERROR
}

export async function checkHealth(): Promise<boolean> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/health`)
    const body = response.ok ? await response.json() : null
    return body?.status === 'OK'
  } catch {
    return false
  }
}

/** Who is signed in right now (from the server's login cookie), or null. */
export async function getCurrentUser(): Promise<User | null> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/auth/me`, { credentials: 'include' })
    if (!response.ok) return null
    const body: unknown = await response.json()
    return isUser(body) ? body : null
  } catch {
    return null
  }
}

export async function login(email: string, password: string): Promise<LoginResult> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ email, password }),
    })
    const body: unknown = await response.json().catch(() => null)
    if (response.ok && isUser(body)) return { ok: true, user: body }
    return { ok: false, message: messageFrom(body) }
  } catch {
    return { ok: false, message: NETWORK_ERROR }
  }
}

export async function logout(): Promise<void> {
  try {
    await fetch(`${API_BASE_URL}/api/auth/logout`, { method: 'POST', credentials: 'include' })
  } catch {
    // Even if the server is unreachable, the screen returns to sign-in.
  }
}
