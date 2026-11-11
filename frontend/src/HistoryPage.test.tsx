import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { HistoryPage } from './HistoryPage'

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

/** A scan saved at a local date/time (month is 0-based: 10 = November). */
const scan = (
  day: number, hour: number, minute: number, status: string, orderNumber: string | null,
  fileName = 'sheet.jpg', reviewStatus = 'Waiting for review', exceptions: string[] = [],
) => ({
  scanId: crypto.randomUUID(),
  fileName,
  uploadedAt: new Date(2026, 10, day, hour, minute).toISOString(),
  status,
  zone: 'North',
  orderNumber,
  deliverTo: 'Test Recipient',
  totalProducts: 1,
  deliveryInstructions: 'x',
  openFlags: 0,
  reviewStatus,
  exceptions,
})

// Week of Mon Nov 9 – Sun Nov 15, 2026, and the week before (Mon Nov 2).
const scans = [
  scan(11, 14, 30, 'Read', 'MF-10003', 'afternoon.jpg'),
  scan(10, 9, 5, 'Read', 'MF-10001', 'morning.jpg', 'Confirmed', ['Left at door']),
  scan(10, 9, 40, 'Failed', null, 'blurry.jpg', 'Denied', ["Couldn't read"]),
  scan(4, 16, 0, 'Read', 'MF-10002'),
]

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

async function openTuesday() {
  fireEvent.click(await screen.findByRole('button', { name: /^Week of Nov 9/ }))
  fireEvent.click(screen.getByRole('button', { name: /^Tuesday, Nov 10/ }))
  return screen.getByRole('table', { name: 'Scans on Tuesday, Nov 10' })
}

describe('All scans page', () => {
  it('lists weeks, newest first, with how many succeeded and failed', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(200, scans)))

    render(<HistoryPage onBack={() => {}} />)

    const weeks = await screen.findAllByRole('button', { name: /^Week of/ })
    expect(weeks.map((w) => w.textContent)).toEqual([
      'Week of Nov 9, 20263 scans · 2 read · 1 failed',
      'Week of Nov 2, 20261 scan · 1 read · 0 failed',
    ])
  })

  it('clicking a week shows its days; clicking a day shows each scan with its time', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(200, scans)))
    render(<HistoryPage onBack={() => {}} />)

    const week = await screen.findByRole('button', { name: /^Week of Nov 9/ })
    expect(week.getAttribute('aria-expanded')).toBe('false')
    expect(screen.queryByRole('button', { name: /^Tuesday/ })).toBeNull()

    fireEvent.click(week)

    expect(week.getAttribute('aria-expanded')).toBe('true')
    const days = screen.getAllByRole('button', { name: /day, Nov/ })
    expect(days.map((d) => d.textContent)).toEqual([
      'Wednesday, Nov 111 scan · 1 read · 0 failed',
      'Tuesday, Nov 102 scans · 1 read · 1 failed',
    ])

    fireEvent.click(screen.getByRole('button', { name: /^Tuesday, Nov 10/ }))

    const table = screen.getByRole('table', { name: 'Scans on Tuesday, Nov 10' })
    expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual([
      'Time recorded', 'Result', 'Order ID', 'File', 'Exceptions / notes',
    ])
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((r) => within(r).getAllByRole('cell').map((c) => c.textContent))).toEqual([
      ['9:05 AM', '✓ Read', 'MF-10001', 'morning.jpg', 'Left at door'],
      ['9:40 AM', "✗ Couldn't read", 'Missing', 'blurry.jpg', "Couldn't read"],
    ])
  })

  it('clicking a scan opens the scanned sheet and whether it was confirmed or denied', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(200, scans)))
    render(<HistoryPage onBack={() => {}} />)
    const table = await openTuesday()

    fireEvent.click(within(table).getByText('morning.jpg'))
    let viewer = screen.getByRole('dialog', { name: 'Sheet for order MF-10001' })
    expect(within(viewer).getByText('Confirmed')).toBeTruthy()
    expect(within(viewer).getByRole('img')).toBeTruthy()
    fireEvent.click(within(viewer).getByRole('button', { name: 'Close' }))

    fireEvent.click(within(table).getByText('blurry.jpg'))
    viewer = screen.getByRole('dialog', { name: 'Sheet blurry.jpg' })
    expect(within(viewer).getByText('Denied')).toBeTruthy()
  })

  it('shows a plain message when nothing has been scanned', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(200, [])))

    render(<HistoryPage onBack={() => {}} />)

    expect(await screen.findByText('Nothing has been scanned in the last 12 weeks.')).toBeTruthy()
  })

  it('explains when the list cannot be loaded', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('network down') }))

    render(<HistoryPage onBack={() => {}} />)

    expect(await screen.findByText("Can't load the scans right now. Please try again in a minute.")).toBeTruthy()
  })

  it('opens from the home screen and goes back', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url === '/api/auth/me'
          ? json(200, { email: 'employee@demo.local', role: 'Employee' })
          : url === '/api/scans/history'
            ? json(200, [])
            : json(200, { status: 'OK' }),
      ),
    )
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: 'See all scans' }))
    expect(screen.getByRole('heading', { name: 'All scans' })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Back' }))
    expect(screen.getByRole('button', { name: 'See all scans' })).toBeTruthy()
  })
})
