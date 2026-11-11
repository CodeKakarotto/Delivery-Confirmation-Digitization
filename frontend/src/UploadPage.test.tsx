import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { UploadPage } from './UploadPage'

const json = (status: number, body: unknown) =>
  new Response(body === null ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

const saved = (fileName: string, duplicate = false) => ({
  fileName,
  saved: true,
  scanId: crypto.randomUUID(),
  duplicate,
  message: duplicate
    ? "Saved. This exact file was uploaded before, so it's marked for a second look."
    : 'Saved.',
})

const rejected = (fileName: string, message: string) => ({
  fileName,
  saved: false,
  scanId: null,
  duplicate: false,
  message,
})

function picture(name: string) {
  return new File([new Uint8Array([0xff, 0xd8, 0xff])], name, { type: 'image/jpeg' })
}

function chooseFiles(...files: File[]) {
  fireEvent.change(screen.getByLabelText('Choose files'), { target: { files } })
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('Scan delivery sheets page', () => {
  it('only allows Upload after files are chosen', () => {
    render(<UploadPage onBack={() => {}} />)

    const upload = screen.getByRole('button', { name: 'Upload' }) as HTMLButtonElement
    expect(upload.disabled).toBe(true)

    chooseFiles(picture('sheet-1.jpg'))

    expect(upload.disabled).toBe(false)
    expect(screen.getByText('1 file chosen')).toBeTruthy()
  })

  it('uploads each file and shows a result for each one', async () => {
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      const file = (init!.body as FormData).get('files') as File
      return file.name === 'sheet-1.jpg'
        ? json(200, { results: [saved('sheet-1.jpg')] })
        : json(200, { results: [rejected('notes.jpg', "This file isn't a JPEG or PNG picture.")] })
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<UploadPage onBack={() => {}} />)

    chooseFiles(picture('sheet-1.jpg'), picture('notes.jpg'))
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    expect(await screen.findByText('Saved.')).toBeTruthy()
    expect(await screen.findByText("This file isn't a JPEG or PNG picture.")).toBeTruthy()
    expect(screen.getByText('sheet-1.jpg')).toBeTruthy()
    expect(screen.getByText('notes.jpg')).toBeTruthy()

    // One upload request per file, sent with the login cookie.
    // (The page also loads the "Orders" list; those requests aren't uploads.)
    const uploads = fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')
    expect(uploads).toHaveLength(2)
    for (const [url, init] of uploads) {
      expect(url).toBe('/api/scans')
      expect(init?.method).toBe('POST')
      expect(init?.credentials).toBe('include')
    }
  })

  it('warns when the same file was uploaded before', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(200, { results: [saved('again.jpg', true)] })))
    render(<UploadPage onBack={() => {}} />)

    chooseFiles(picture('again.jpg'))
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    expect(
      await screen.findByText(
        "Saved. This exact file was uploaded before, so it's marked for a second look.",
      ),
    ).toBeTruthy()
  })

  it('explains when the server cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('network down') }))
    render(<UploadPage onBack={() => {}} />)

    chooseFiles(picture('sheet.jpg'))
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    expect(
      await screen.findByText("Can't reach the server. Please try again in a minute."),
    ).toBeTruthy()
  })

  it('explains when the sign-in has expired', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(401, null)))
    render(<UploadPage onBack={() => {}} />)

    chooseFiles(picture('sheet.jpg'))
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    expect(
      await screen.findByText('Your sign-in has expired. Please sign out and sign in again.'),
    ).toBeTruthy()
  })

  it('shows file names as plain text, never as HTML', async () => {
    const evil = '<img src=x onerror="alert(1)">.jpg'
    vi.stubGlobal('fetch', vi.fn(async () => json(200, { results: [saved(evil)] })))
    const { container } = render(<UploadPage onBack={() => {}} />)

    chooseFiles(picture(evil))
    fireEvent.click(screen.getByRole('button', { name: 'Upload' }))

    expect(await screen.findByText(evil)).toBeTruthy()
    expect(container.querySelector('img')).toBeNull()
  })

  it('opens from the home screen and goes back', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url === '/api/auth/me'
          ? json(200, { email: 'employee@demo.local', role: 'Employee' })
          : json(200, { status: 'OK' }),
      ),
    )
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: 'Scan delivery sheets' }))
    expect(screen.getByRole('heading', { name: 'Scan delivery sheets' })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Back' }))
    expect(screen.getByRole('button', { name: 'Scan delivery sheets' })).toBeTruthy()
  })
})
