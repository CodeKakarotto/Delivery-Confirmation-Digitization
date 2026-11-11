import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { UploadPage } from './UploadPage'

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const scan = (overrides: Record<string, unknown> = {}) => ({
  scanId: crypto.randomUUID(),
  fileName: 'sheet.jpg',
  uploadedAt: new Date(2026, 10, 10, 9, 5).toISOString(),
  status: 'Read',
  zone: 'North',
  orderNumber: 'MF-10001',
  deliverTo: 'Test Recipient 1, 101 Example St',
  totalProducts: 2,
  deliveryInstructions: 'Leave at side door',
  openFlags: 0,
  reviewStatus: 'Waiting for review',
  exceptions: [],
  ...overrides,
})

const unread = { zone: null, orderNumber: null, deliverTo: null, totalProducts: null, deliveryInstructions: null }

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('Orders box under the upload page', () => {
  it('lists recently saved uploads with the ticket fields and an exceptions column', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        json(200, [
          scan({
            orderNumber: 'MF-10002', zone: 'South', deliverTo: 'Test Recipient 2', totalProducts: 3,
            deliveryInstructions: 'Nobody home, left with neighbor', exceptions: ['Left with neighbor', 'Nobody home'],
          }),
          scan({ orderNumber: 'MF-10001' }),
        ]),
      ),
    )

    render(<UploadPage onBack={() => {}} />)

    const box = await screen.findByRole('region', { name: 'Orders' })
    const table = within(box).getByRole('table')
    expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual([
      '#', 'Zone', 'Order ID', 'Deliver to', 'Total products', 'Delivery instructions', 'Exceptions / notes',
    ])
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    const cells = within(rows[0]).getAllByRole('cell')
    expect(cells.slice(0, 6).map((c) => c.textContent)).toEqual([
      '1', 'South', 'MF-10002', 'Test Recipient 2', '3', 'Nobody home, left with neighbor',
    ])
    expect(within(cells[6]).getByText('Left with neighbor')).toBeTruthy()
    expect(within(cells[6]).getByText('Nobody home')).toBeTruthy()
    expect(within(rows[1]).getAllByRole('cell')[6].textContent).toBe('—')
  })

  it('says when there are no uploads yet', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(200, [])))

    render(<UploadPage onBack={() => {}} />)

    expect(await screen.findByText('No uploads yet.')).toBeTruthy()
  })

  it('never leaves the Order ID blank: reading, or clearly missing', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        json(200, [
          scan({ status: 'Reading', ...unread }),
          scan({ status: 'Failed', ...unread }),
          scan({ status: 'Read', orderNumber: null }),
        ]),
      ),
    )

    render(<UploadPage onBack={() => {}} />)

    const rows = (await screen.findAllByRole('row')).slice(1)
    const orderIds = rows.map((r) => within(r).getAllByRole('cell')[2].textContent)
    expect(orderIds).toEqual(['Reading…', 'Missing', 'Missing'])
    expect(screen.getByText("Couldn't read")).toBeTruthy()
  })

  it('refreshes by itself while a sheet is still being read', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    let calls = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        calls++
        return calls === 1
          ? json(200, [scan({ status: 'Reading', orderNumber: null })])
          : json(200, [scan({ status: 'Read', orderNumber: 'MF-10009' })])
      }),
    )

    render(<UploadPage onBack={() => {}} />)
    expect(await screen.findByText('Reading…')).toBeTruthy()

    await vi.advanceTimersByTimeAsync(3500)

    expect(await screen.findByText('MF-10009')).toBeTruthy()
  })

  it('refreshes the list after uploading', async () => {
    const fetchMock = vi.fn(async (url: string, _init?: RequestInit) =>
      url === '/api/scans'
        ? json(200, { results: [{ fileName: 'new.jpg', saved: true, scanId: 'x', duplicate: false, message: 'Saved.' }] })
        : json(200, []),
    )
    vi.stubGlobal('fetch', fetchMock)
    render(<UploadPage onBack={() => {}} />)
    await screen.findByText('No uploads yet.')

    fireEvent.change(screen.getByLabelText('Choose files'), {
      target: { files: [new File([new Uint8Array([0xff, 0xd8, 0xff])], 'new.jpg', { type: 'image/jpeg' })] },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))
    await screen.findByText('Saved.')

    const recentCalls = fetchMock.mock.calls.filter(([url]) => url === '/api/scans/recent')
    expect(recentCalls.length).toBeGreaterThanOrEqual(2)
    expect(recentCalls[0][1]).toEqual({ credentials: 'include' })
  })

  it('shows ticket text as plain text, never as HTML', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(200, [scan({ deliverTo: '<img src=x onerror="alert(1)">' })])))

    const { container } = render(<UploadPage onBack={() => {}} />)

    expect(await screen.findByText('<img src=x onerror="alert(1)">')).toBeTruthy()
    expect(container.querySelector('img')).toBeNull()
  })
})

describe('Seeing the scanned sheet', () => {
  it('clicking an order opens the scanned paper with its review result', async () => {
    const confirmed = scan({ fileName: 'sheet-7.jpg', orderNumber: 'MF-10007', reviewStatus: 'Confirmed', exceptions: ['Left at door'] })
    vi.stubGlobal('fetch', vi.fn(async () => json(200, [confirmed])))
    render(<UploadPage onBack={() => {}} />)

    fireEvent.click(await screen.findByRole('button', { name: 'View sheet 1' }))

    const viewer = screen.getByRole('dialog', { name: 'Sheet for order MF-10007' })
    const image = within(viewer).getByRole('img', { name: 'Scanned delivery sheet sheet-7.jpg' }) as HTMLImageElement
    expect(image.getAttribute('src')).toBe(`/api/scans/${confirmed.scanId}/image`)
    expect(within(viewer).getByText('Confirmed')).toBeTruthy()
    expect(within(viewer).getByText('Left at door')).toBeTruthy()
  })

  it('clicking anywhere on the row also opens it, and Close or Escape closes it', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(200, [scan({ orderNumber: 'MF-10008', reviewStatus: 'Denied' })])))
    render(<UploadPage onBack={() => {}} />)

    fireEvent.click(await screen.findByText('MF-10008'))
    expect(within(screen.getByRole('dialog')).getByText('Denied')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).toBeNull()

    fireEvent.click(screen.getByText('MF-10008'))
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('a sheet without an order ID still opens, titled by its file name', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(200, [scan({ status: 'Failed', fileName: 'blurry.jpg', ...unread })])))
    render(<UploadPage onBack={() => {}} />)

    fireEvent.click(await screen.findByRole('button', { name: 'View sheet 1' }))

    const viewer = screen.getByRole('dialog', { name: 'Sheet blurry.jpg' })
    expect(within(viewer).getByText('Waiting for review')).toBeTruthy()
    expect(within(viewer).getByText("Couldn't be read automatically")).toBeTruthy()
  })
})
