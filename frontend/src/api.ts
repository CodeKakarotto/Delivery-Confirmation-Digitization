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

function messageFrom(body: unknown, fallback: string = GENERIC_LOGIN_ERROR): string {
  if (typeof body === 'object' && body !== null) {
    const message = (body as Record<string, unknown>).message
    if (typeof message === 'string' && message.length > 0) return message
  }
  return fallback
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

export type UploadResult = {
  fileName: string
  saved: boolean
  duplicate: boolean
  message: string
}

const SIGN_IN_EXPIRED = 'Your sign-in has expired. Please sign out and sign in again.'
const GENERIC_UPLOAD_ERROR = 'Upload failed. Please try again.'

function isUploadResult(value: unknown): value is UploadResult {
  if (typeof value !== 'object' || value === null) return false
  const v = value as Record<string, unknown>
  return (
    typeof v.fileName === 'string' &&
    typeof v.saved === 'boolean' &&
    typeof v.duplicate === 'boolean' &&
    typeof v.message === 'string'
  )
}

/** Uploads one scanned sheet. Always resolves with a result to show the employee. */
export async function uploadScan(file: File): Promise<UploadResult> {
  const failed = (message: string): UploadResult => ({
    fileName: file.name,
    saved: false,
    duplicate: false,
    message,
  })

  try {
    const form = new FormData()
    form.append('files', file)
    const response = await fetch(`${API_BASE_URL}/api/scans`, {
      method: 'POST',
      credentials: 'include',
      body: form,
    })
    if (response.status === 401 || response.status === 403) return failed(SIGN_IN_EXPIRED)
    if (response.status === 413) return failed('This file is larger than 10 MB.')

    const body: unknown = await response.json().catch(() => null)
    if (response.ok) {
      const results = (body as { results?: unknown[] } | null)?.results
      const first = Array.isArray(results) ? results[0] : undefined
      return isUploadResult(first) ? first : failed(GENERIC_UPLOAD_ERROR)
    }
    return failed(messageFrom(body, GENERIC_UPLOAD_ERROR))
  } catch {
    return failed(NETWORK_ERROR)
  }
}

/** One saved scan in a list. status: 'Reading' (not read yet), 'Read', or 'Failed'. */
export type ScanSummary = {
  scanId: string
  confirmationId?: string
  fileName: string
  uploadedAt: string
  status: 'Reading' | 'Read' | 'Failed'
  zone: string | null
  orderNumber: string | null
  deliverTo: string | null
  totalProducts: number | null
  deliveryInstructions: string | null
  openFlags: number
  /** A person's decision: 'Waiting for review', 'Confirmed', 'Denied', 'Sent to manager', 'Voided'. */
  reviewStatus?: string
  /** Short labels for the "Exceptions / notes" column. */
  exceptions?: string[]
}

/** Address of a scan's picture. Only works for a signed-in Employee or Manager. */
export const scanImageUrl = (scanId: string) => `${API_BASE_URL}/api/scans/${encodeURIComponent(scanId)}/image`

function isScanSummary(value: unknown): value is ScanSummary {
  if (typeof value !== 'object' || value === null) return false
  const v = value as Record<string, unknown>
  return (
    typeof v.scanId === 'string' &&
    typeof v.fileName === 'string' &&
    typeof v.uploadedAt === 'string' &&
    (v.status === 'Reading' || v.status === 'Read' || v.status === 'Failed')
  )
}

async function getScanList(path: string): Promise<ScanSummary[] | null> {
  try {
    const response = await fetch(`${API_BASE_URL}${path}`, { credentials: 'include' })
    if (!response.ok) return null
    const body: unknown = await response.json()
    return Array.isArray(body) ? body.filter(isScanSummary) : null
  } catch {
    return null
  }
}

/** Most recently saved uploads, newest first. Null if they can't be loaded. */
export const getRecentScans = () => getScanList('/api/scans/recent')

/** Every scan from the last 12 weeks, newest first. Null if they can't be loaded. */
export const getScanHistory = () => getScanList('/api/scans/history')

// ---------- Check & approve (Step 7) ----------

export type OriginalReading = {
  zone: string | null
  zoneConfidence: number | null
  orderNumber: string | null
  orderNumberConfidence: number | null
  deliverTo: string | null
  deliverToConfidence: number | null
  totalProducts: string | null
  totalProductsConfidence: number | null
  deliveryInstructions: string | null
  deliveryInstructionsConfidence: number | null
  signatureMarkDetected: boolean | null
  signatureConfidence: number | null
  ocrFailed: boolean
}

export type ReviewDetail = {
  confirmationId: string
  scanId: string
  fileName: string
  uploadedAt: string
  readStatus: 'Reading' | 'Read' | 'Failed'
  status: 'Pending' | 'Approved' | 'Rejected' | 'Flagged' | 'Voided'
  reviewStatus: string
  version: string
  zone: string | null
  orderNumber: string | null
  deliverTo: string | null
  totalProducts: number | null
  deliveryInstructions: string | null
  signaturePresent: boolean | null
  exceptionNotes: string | null
  reviewerNote: string | null
  reviewedBy: string | null
  reviewedAt: string | null
  original: OriginalReading | null
  flags: { type: string; field: string | null; message: string }[]
  exceptions: string[]
}

export type ReviewEdit = {
  version: string
  zone: string | null
  orderNumber: string | null
  deliverTo: string | null
  totalProducts: number | null
  deliveryInstructions: string | null
  signaturePresent: boolean | null
  exceptionNotes: string | null
}

export type ReviewActionResult =
  | { ok: true; detail: ReviewDetail }
  | { ok: false; message: string; reasons: string[] }

/** Sheets waiting for review, oldest first. Null if they can't be loaded. */
export const getWaitingSheets = () => getScanList('/api/confirmations/waiting')

export async function getReview(confirmationId: string): Promise<ReviewDetail | null> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/confirmations/${encodeURIComponent(confirmationId)}`, {
      credentials: 'include',
    })
    return response.ok ? ((await response.json()) as ReviewDetail) : null
  } catch {
    return null
  }
}

async function reviewAction(method: 'PUT' | 'POST', path: string, body: unknown): Promise<ReviewActionResult> {
  try {
    const response = await fetch(`${API_BASE_URL}${path}`, {
      method,
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(body),
    })
    const payload: unknown = await response.json().catch(() => null)
    if (response.ok && payload) return { ok: true, detail: payload as ReviewDetail }
    if (response.status === 401 || response.status === 403) return { ok: false, message: SIGN_IN_EXPIRED, reasons: [] }
    const p = (payload ?? {}) as { message?: unknown; reasons?: unknown }
    return {
      ok: false,
      message: typeof p.message === 'string' ? p.message : 'Something went wrong. Please try again.',
      reasons: Array.isArray(p.reasons) ? p.reasons.filter((r): r is string => typeof r === 'string') : [],
    }
  } catch {
    return { ok: false, message: NETWORK_ERROR, reasons: [] }
  }
}

const reviewPath = (id: string, action = '') => `/api/confirmations/${encodeURIComponent(id)}${action}`

export const saveReview = (id: string, edit: ReviewEdit) => reviewAction('PUT', reviewPath(id), edit)
export const approveReview = (id: string, edit: ReviewEdit) => reviewAction('POST', reviewPath(id, '/approve'), edit)
export const rejectReview = (id: string, version: string, reason: string) =>
  reviewAction('POST', reviewPath(id, '/reject'), { version, reason })
export const sendToManager = (id: string, version: string, note: string) =>
  reviewAction('POST', reviewPath(id, '/send-to-manager'), { version, note })

export async function logout(): Promise<void> {
  try {
    await fetch(`${API_BASE_URL}/api/auth/logout`, { method: 'POST', credentials: 'include' })
  } catch {
    // Even if the server is unreachable, the screen returns to sign-in.
  }
}
