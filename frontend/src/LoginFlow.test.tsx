import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

type Route = (init?: RequestInit) => Response

const json = (status: number, body: unknown) =>
  new Response(body === null ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

/** Fake backend: each URL gets its own canned answer. */
function fakeBackend(routes: Record<string, Route>) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    const route = routes[url]
    return route ? route(init) : json(404, null)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const healthy = { '/api/health': () => json(200, { status: 'OK' }) }
const notSignedIn = { '/api/auth/me': () => json(401, null) }

function signIn(email: string, password: string) {
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: email } })
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } })
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('Login screen', () => {
  it('shows the sign-in form when nobody is signed in', async () => {
    fakeBackend({ ...healthy, ...notSignedIn })

    render(<App />)

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeTruthy()
    expect(screen.getByLabelText('Email')).toBeTruthy()
    expect(screen.getByLabelText('Password')).toBeTruthy()
  })

  it('signs in and shows who is signed in', async () => {
    const fetchMock = fakeBackend({
      ...healthy,
      ...notSignedIn,
      '/api/auth/login': () => json(200, { email: 'employee@demo.local', role: 'Employee' }),
    })
    render(<App />)
    await screen.findByRole('button', { name: 'Sign in' })

    signIn('employee@demo.local', 'secret-pass')

    expect(await screen.findByText('Signed in as employee@demo.local (Employee)')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull()

    const loginCall = fetchMock.mock.calls.find(([url]) => url === '/api/auth/login')!
    expect(loginCall[1]?.method).toBe('POST')
    expect(loginCall[1]?.credentials).toBe('include')
    expect(JSON.parse(loginCall[1]?.body as string)).toEqual({
      email: 'employee@demo.local',
      password: 'secret-pass',
    })
  })

  it('shows the server message for a wrong password', async () => {
    fakeBackend({
      ...healthy,
      ...notSignedIn,
      '/api/auth/login': () => json(401, { message: 'Incorrect email or password.' }),
    })
    render(<App />)
    await screen.findByRole('button', { name: 'Sign in' })

    signIn('employee@demo.local', 'wrong')

    expect((await screen.findByRole('alert')).textContent).toBe('Incorrect email or password.')
  })

  it('tells drivers they cannot use the app', async () => {
    fakeBackend({
      ...healthy,
      ...notSignedIn,
      '/api/auth/login': () =>
        json(403, { message: "Driver accounts can't use the delivery confirmation app." }),
    })
    render(<App />)
    await screen.findByRole('button', { name: 'Sign in' })

    signIn('driver@demo.local', 'secret-pass')

    expect((await screen.findByRole('alert')).textContent).toBe(
      "Driver accounts can't use the delivery confirmation app.",
    )
  })

  it('shows a plain message when the server cannot be reached', async () => {
    fakeBackend({
      ...healthy,
      ...notSignedIn,
      '/api/auth/login': () => {
        throw new TypeError('network down')
      },
    })
    render(<App />)
    await screen.findByRole('button', { name: 'Sign in' })

    signIn('employee@demo.local', 'secret-pass')

    expect((await screen.findByRole('alert')).textContent).toBe(
      "Can't reach the server. Please try again in a minute.",
    )
  })

  it('shows server text as plain text, never as HTML', async () => {
    fakeBackend({
      ...healthy,
      ...notSignedIn,
      '/api/auth/login': () => json(401, { message: '<img src=x onerror="alert(1)">' }),
    })
    render(<App />)
    await screen.findByRole('button', { name: 'Sign in' })

    signIn('employee@demo.local', 'wrong')

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toBe('<img src=x onerror="alert(1)">')
    expect(alert.querySelector('img')).toBeNull()
  })

  it('skips the sign-in form when already signed in', async () => {
    fakeBackend({
      ...healthy,
      '/api/auth/me': () => json(200, { email: 'manager@demo.local', role: 'Manager' }),
    })

    render(<App />)

    expect(await screen.findByText('Signed in as manager@demo.local (Manager)')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull()
  })

  it('signs out and returns to the sign-in form', async () => {
    const fetchMock = fakeBackend({
      ...healthy,
      '/api/auth/me': () => json(200, { email: 'employee@demo.local', role: 'Employee' }),
      '/api/auth/logout': () => json(204, null),
    })
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: 'Sign out' }))

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeTruthy()
    expect(fetchMock.mock.calls.some(([url]) => url === '/api/auth/logout')).toBe(true)
  })
})
