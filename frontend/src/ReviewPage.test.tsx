import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { ReviewPage } from './ReviewPage'
import { ReviewQueuePage } from './ReviewQueuePage'

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const ID = '11111111-1111-1111-1111-111111111111'
const SCAN = '22222222-2222-2222-2222-222222222222'
const VERSION = '33333333-3333-3333-3333-333333333333'

const detail = (overrides: Record<string, unknown> = {}) => ({
  confirmationId: ID,
  scanId: SCAN,
  fileName: 'sheet-1.jpg',
  uploadedAt: new Date(2026, 10, 10, 9, 5).toISOString(),
  readStatus: 'Read',
  status: 'Pending',
  reviewStatus: 'Waiting for review',
  version: VERSION,
  zone: 'Nrth',
  orderNumber: 'MF-10001',
  deliverTo: 'Test Recipient 1, 101 Example St',
  totalProducts: 2,
  deliveryInstructions: 'Leave at side door',
  signaturePresent: null,
  exceptionNotes: null,
  reviewerNote: null,
  reviewedBy: null,
  reviewedAt: null,
  original: {
    zone: 'Nrth', zoneConfidence: 0.61, orderNumber: 'MF-10001', orderNumberConfidence: 0.95,
    deliverTo: 'Test Recipient 1, 101 Example St', deliverToConfidence: 0.96, totalProducts: '2',
    totalProductsConfidence: 0.96, deliveryInstructions: 'Leave at side door', deliveryInstructionsConfidence: 0.96,
    signatureMarkDetected: true, signatureConfidence: 0.9, ocrFailed: false,
  },
  flags: [{ type: 'LowConfidence', field: 'Zone', message: 'Please double-check the zone. That part of the scan was hard to read.' }],
  exceptions: ['Check zone'],
  ...overrides,
})

type Call = [string, RequestInit | undefined]

function backend(routes: Record<string, (init?: RequestInit) => Response>) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    const key = `${init?.method ?? 'GET'} ${url}`
    const route = routes[key]
    return route ? route(init) : json(404, null)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const bodyOf = (calls: Call[], key: string) => {
  const call = calls.find(([url, init]) => `${init?.method ?? 'GET'} ${url}` === key)!
  return JSON.parse(call[1]!.body as string)
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('Check & approve: waiting list', () => {
  it('lists sheets waiting for review and opens one when clicked', async () => {
    backend({
      'GET /api/confirmations/waiting': () =>
        json(200, [
          { scanId: SCAN, confirmationId: ID, fileName: 'a.jpg', uploadedAt: new Date(2026, 10, 10, 9, 5).toISOString(), status: 'Read', zone: 'North', orderNumber: 'MF-10001', deliverTo: 'Test Recipient 1', totalProducts: 2, deliveryInstructions: 'x', openFlags: 1, reviewStatus: 'Waiting for review', exceptions: ['No signature'] },
        ]),
    })
    const onOpen = vi.fn()
    render(<ReviewQueuePage onBack={() => {}} onOpen={onOpen} />)

    const table = await screen.findByRole('table', { name: 'Sheets waiting for review' })
    const row = within(table).getAllByRole('row')[1]
    expect(within(row).getByText('MF-10001')).toBeTruthy()
    expect(within(row).getByText('No signature')).toBeTruthy()

    fireEvent.click(within(row).getByText('MF-10001'))
    expect(onOpen).toHaveBeenCalledWith(ID)
  })

  it('says when nothing is waiting', async () => {
    backend({ 'GET /api/confirmations/waiting': () => json(200, []) })

    render(<ReviewQueuePage onBack={() => {}} onOpen={() => {}} />)

    expect(await screen.findByText('Nothing is waiting for review.')).toBeTruthy()
  })

  it('opens from the home screen', async () => {
    backend({
      'GET /api/auth/me': () => json(200, { email: 'employee@demo.local', role: 'Employee' }),
      'GET /api/health': () => json(200, { status: 'OK' }),
      'GET /api/confirmations/waiting': () => json(200, []),
    })
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: 'Check & approve' }))

    expect(screen.getByRole('heading', { name: 'Check & approve' })).toBeTruthy()
  })
})

describe('Check & approve: one sheet', () => {
  it('shows the scanned sheet beside the fields, the problems, and what the scan originally said', async () => {
    backend({ [`GET /api/confirmations/${ID}`]: () => json(200, detail()) })

    render(<ReviewPage confirmationId={ID} onBack={() => {}} onDone={() => {}} />)

    const image = (await screen.findByRole('img', { name: 'Scanned delivery sheet sheet-1.jpg' })) as HTMLImageElement
    expect(image.getAttribute('src')).toBe(`/api/scans/${SCAN}/image`)
    expect((screen.getByLabelText('Zone') as HTMLInputElement).value).toBe('Nrth')
    expect((screen.getByLabelText('Order ID') as HTMLInputElement).value).toBe('MF-10001')
    expect((screen.getByLabelText('Total products') as HTMLInputElement).value).toBe('2')
    expect(screen.getByText('Please double-check the zone. That part of the scan was hard to read.')).toBeTruthy()
    expect(screen.getByText('Read from the sheet: Nrth (hard to read)')).toBeTruthy()
    expect(screen.getByText('The scan shows a pen mark in the signature box.')).toBeTruthy()
  })

  it('Approve sends the corrected values and goes back with a message', async () => {
    const fetchMock = backend({
      [`GET /api/confirmations/${ID}`]: () => json(200, detail()),
      [`POST /api/confirmations/${ID}/approve`]: () => json(200, detail({ status: 'Approved', reviewStatus: 'Confirmed' })),
    })
    const onDone = vi.fn()
    render(<ReviewPage confirmationId={ID} onBack={() => {}} onDone={onDone} />)

    fireEvent.change(await screen.findByLabelText('Zone'), { target: { value: 'North' } })
    fireEvent.click(screen.getByLabelText('Yes'))
    fireEvent.change(screen.getByLabelText('Exceptions / notes'), { target: { value: 'Gate code 1234' } })
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }))

    await vi.waitFor(() => expect(onDone).toHaveBeenCalledWith('Approved order MF-10001.'))
    const call = fetchMock.mock.calls.find(([url]) => url === `/api/confirmations/${ID}/approve`)!
    expect(call[1]?.credentials).toBe('include')
    expect(bodyOf(fetchMock.mock.calls as Call[], `POST /api/confirmations/${ID}/approve`)).toEqual({
      version: VERSION, zone: 'North', orderNumber: 'MF-10001', deliverTo: 'Test Recipient 1, 101 Example St',
      totalProducts: 2, deliveryInstructions: 'Leave at side door', signaturePresent: true, exceptionNotes: 'Gate code 1234',
    })
  })

  it('when approval is refused, every reason is listed', async () => {
    backend({
      [`GET /api/confirmations/${ID}`]: () => json(200, detail()),
      [`POST /api/confirmations/${ID}/approve`]: () =>
        json(400, { message: "This sheet can't be approved yet.", reasons: ['Zone is missing.', 'Please answer "Signature present": yes or no.'] }),
    })
    render(<ReviewPage confirmationId={ID} onBack={() => {}} onDone={() => {}} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText("This sheet can't be approved yet.")).toBeTruthy()
    expect(within(alert).getByText('Zone is missing.')).toBeTruthy()
    expect(within(alert).getByText('Please answer "Signature present": yes or no.')).toBeTruthy()
  })

  it('explains when someone else changed the record first', async () => {
    backend({
      [`GET /api/confirmations/${ID}`]: () => json(200, detail()),
      [`PUT /api/confirmations/${ID}`]: () => json(409, { message: 'Someone else changed this record. Please reload it to see their changes.' }),
    })
    render(<ReviewPage confirmationId={ID} onBack={() => {}} onDone={() => {}} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Save changes' }))

    expect(await screen.findByText('Someone else changed this record. Please reload it to see their changes.')).toBeTruthy()
  })

  it('Save keeps the changes and stays on the sheet', async () => {
    const fetchMock = backend({
      [`GET /api/confirmations/${ID}`]: () => json(200, detail()),
      [`PUT /api/confirmations/${ID}`]: () => json(200, detail({ zone: 'North', flags: [], version: '44444444-4444-4444-4444-444444444444' })),
    })
    const onDone = vi.fn()
    render(<ReviewPage confirmationId={ID} onBack={() => {}} onDone={onDone} />)

    fireEvent.change(await screen.findByLabelText('Zone'), { target: { value: 'North' } })
    fireEvent.click(screen.getByLabelText('No'))
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

    expect(await screen.findByText('Saved.')).toBeTruthy()
    expect(onDone).not.toHaveBeenCalled()
    expect(bodyOf(fetchMock.mock.calls as Call[], `PUT /api/confirmations/${ID}`).signaturePresent).toBe(false)
  })

  it('Reject asks for an optional reason, then goes back', async () => {
    const fetchMock = backend({
      [`GET /api/confirmations/${ID}`]: () => json(200, detail()),
      [`POST /api/confirmations/${ID}/reject`]: () => json(200, detail({ status: 'Rejected' })),
    })
    const onDone = vi.fn()
    render(<ReviewPage confirmationId={ID} onBack={() => {}} onDone={onDone} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Reject' }))
    fireEvent.change(screen.getByLabelText('Reason (optional)'), { target: { value: 'Wrong sheet' } })
    fireEvent.click(screen.getByRole('button', { name: 'Reject this sheet' }))

    await vi.waitFor(() => expect(onDone).toHaveBeenCalledWith('Rejected the sheet for order MF-10001.'))
    expect(bodyOf(fetchMock.mock.calls as Call[], `POST /api/confirmations/${ID}/reject`)).toEqual({ version: VERSION, reason: 'Wrong sheet' })
  })

  it('Send to manager needs a note before it can be sent', async () => {
    const fetchMock = backend({
      [`GET /api/confirmations/${ID}`]: () => json(200, detail()),
      [`POST /api/confirmations/${ID}/send-to-manager`]: () => json(200, detail({ status: 'Flagged' })),
    })
    const onDone = vi.fn()
    render(<ReviewPage confirmationId={ID} onBack={() => {}} onDone={onDone} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Send to manager' }))
    const send = screen.getByRole('button', { name: 'Send it' }) as HTMLButtonElement
    expect(send.disabled).toBe(true)

    fireEvent.change(screen.getByLabelText('Note for the manager'), { target: { value: 'Order not in the list' } })
    expect(send.disabled).toBe(false)
    fireEvent.click(send)

    await vi.waitFor(() => expect(onDone).toHaveBeenCalledWith('Sent order MF-10001 to a manager.'))
    expect(bodyOf(fetchMock.mock.calls as Call[], `POST /api/confirmations/${ID}/send-to-manager`)).toEqual({ version: VERSION, note: 'Order not in the list' })
  })

  it('an already-reviewed sheet is shown read-only', async () => {
    backend({
      [`GET /api/confirmations/${ID}`]: () =>
        json(200, detail({ status: 'Approved', reviewStatus: 'Confirmed', reviewedBy: 'manager@demo.local', flags: [] })),
    })
    render(<ReviewPage confirmationId={ID} onBack={() => {}} onDone={() => {}} />)

    expect(await screen.findByText('Confirmed by manager@demo.local')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Approve' })).toBeNull()
    expect((screen.getByLabelText('Zone') as HTMLInputElement).readOnly).toBe(true)
  })
})
