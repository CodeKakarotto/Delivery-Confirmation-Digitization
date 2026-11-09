import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('App health check', () => {
  it('shows "API status: OK" when the backend responds', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ status: 'OK' }), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)

    expect(await screen.findByText('API status: OK')).toBeTruthy()
    expect(fetchMock).toHaveBeenCalledWith('/api/health')
  })

  it('shows "API status: unavailable" when the backend cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network down')))

    render(<App />)

    expect(await screen.findByText('API status: unavailable')).toBeTruthy()
  })
})
