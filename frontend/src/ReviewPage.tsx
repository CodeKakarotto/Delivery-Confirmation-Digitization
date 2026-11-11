import { useEffect, useState, type ReactNode } from 'react'
import {
  approveReview,
  getReview,
  rejectReview,
  saveReview,
  scanImageUrl,
  sendToManager,
  type ReviewActionResult,
  type ReviewDetail,
  type ReviewEdit,
} from './api'

type Props = {
  confirmationId: string
  onBack: () => void
  /** Called after Approve / Reject / Send to manager, with a message for the list. */
  onDone: (message: string) => void
}

type Form = {
  zone: string
  orderNumber: string
  deliverTo: string
  totalProducts: string
  deliveryInstructions: string
  signaturePresent: boolean | null
  exceptionNotes: string
}

const LOW_CONFIDENCE = 0.8

const formFrom = (d: ReviewDetail): Form => ({
  zone: d.zone ?? '',
  orderNumber: d.orderNumber ?? '',
  deliverTo: d.deliverTo ?? '',
  totalProducts: d.totalProducts === null ? '' : String(d.totalProducts),
  deliveryInstructions: d.deliveryInstructions ?? '',
  signaturePresent: d.signaturePresent,
  exceptionNotes: d.exceptionNotes ?? '',
})

const orNull = (s: string) => (s.trim() === '' ? null : s.trim())

/** "Check & approve" for one sheet: the scan on the left, the fields on the right. */
export function ReviewPage({ confirmationId, onBack, onDone }: Props) {
  const [detail, setDetail] = useState<ReviewDetail | null | undefined>(undefined)
  const [form, setForm] = useState<Form | null>(null)
  const [error, setError] = useState<{ message: string; reasons: string[] } | null>(null)
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false)
  const [asking, setAsking] = useState<'reject' | 'manager' | null>(null)
  const [reason, setReason] = useState('')

  useEffect(() => {
    let cancelled = false
    getReview(confirmationId).then((d) => {
      if (cancelled) return
      setDetail(d)
      if (d) setForm(formFrom(d))
    })
    return () => {
      cancelled = true
    }
  }, [confirmationId])

  if (detail === undefined) return <section className="card"><p>Loading…</p></section>
  if (detail === null || form === null)
    return (
      <section className="card">
        <p className="error">Can't load this sheet right now.</p>
        <button type="button" onClick={onBack}>
          <span aria-hidden="true">←</span> Back
        </button>
      </section>
    )

  const editable = detail.status === 'Pending'
  const original = detail.original
  const update = (patch: Partial<Form>) => {
    setForm({ ...form, ...patch })
    setSaved(false)
  }

  const edit = (): ReviewEdit => {
    const total = form.totalProducts.trim()
    return {
      version: detail.version,
      zone: orNull(form.zone),
      orderNumber: orNull(form.orderNumber),
      deliverTo: orNull(form.deliverTo),
      totalProducts: total === '' || Number.isNaN(Number(total)) ? null : Math.trunc(Number(total)),
      deliveryInstructions: orNull(form.deliveryInstructions),
      signaturePresent: form.signaturePresent,
      exceptionNotes: orNull(form.exceptionNotes),
    }
  }

  async function run(action: () => Promise<ReviewActionResult>, after: (d: ReviewDetail) => void) {
    setBusy(true)
    setError(null)
    setSaved(false)
    const result = await action()
    setBusy(false)
    if (result.ok) after(result.detail)
    else setError({ message: result.message, reasons: result.reasons })
  }

  const order = (d: ReviewDetail) => d.orderNumber ?? orNull(form.orderNumber)

  /** "Read from the sheet: …" under a field, when it differs or was hard to read. */
  function fromSheet(current: string, asRead: string | null | undefined, confidence: number | null | undefined): ReactNode {
    if (!original || asRead === null || asRead === undefined) return null
    const differs = current.trim().toLowerCase() !== asRead.trim().toLowerCase()
    const hard = confidence !== null && confidence !== undefined && confidence < LOW_CONFIDENCE
    if (!differs && !hard) return null
    return (
      <span className={`from-sheet ${hard ? 'from-sheet-hard' : ''}`}>
        Read from the sheet: {asRead}
        {hard ? ' (hard to read)' : ''}
      </span>
    )
  }

  const field = (
    id: keyof Form,
    label: string,
    hint: ReactNode,
    options: { multiline?: boolean; numeric?: boolean } = {},
  ) => {
    const value = form[id] as string
    const props = {
      id: `review-${id}`,
      value,
      readOnly: !editable,
      onChange: (e: { target: { value: string } }) => update({ [id]: e.target.value } as Partial<Form>),
    }
    return (
      <div className="review-field">
        <label htmlFor={props.id}>{label}</label>
        {options.multiline ? (
          <textarea rows={2} {...props} />
        ) : (
          <input type={options.numeric ? 'number' : 'text'} min={options.numeric ? 1 : undefined} {...props} />
        )}
        {hint}
      </div>
    )
  }

  const signatureHint =
    original?.signatureMarkDetected === true
      ? 'The scan shows a pen mark in the signature box.'
      : original?.signatureMarkDetected === false
        ? 'The scan shows no pen mark in the signature box.'
        : "The signature box couldn't be checked."

  return (
    <section className="card">
      <h2>Check &amp; approve</h2>
      <p className="muted">
        {detail.fileName} · {detail.readStatus === 'Failed' ? "couldn't be read automatically – please type the details" : 'compare the fields with the sheet'}
      </p>

      {!editable && (
        <p className="notice">
          {detail.reviewStatus}
          {detail.reviewedBy ? ` by ${detail.reviewedBy}` : ''}
          {detail.reviewerNote ? ` – "${detail.reviewerNote}"` : ''}
        </p>
      )}

      {detail.flags.length > 0 && (
        <div className="flag-box">
          <strong>Please check:</strong>
          <ul>
            {detail.flags.map((f) => (
              <li key={`${f.type}-${f.field ?? ''}`}>{f.message}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="review-layout">
        <div className="review-scan">
          <img
            className="viewer-image"
            src={scanImageUrl(detail.scanId)}
            alt={`Scanned delivery sheet ${detail.fileName}`}
          />
          <a className="full-size-link" href={scanImageUrl(detail.scanId)} target="_blank" rel="noreferrer">
            Open full size
          </a>
        </div>

        <div className="review-form">
          {field('zone', 'Zone', fromSheet(form.zone, original?.zone, original?.zoneConfidence))}
          {field('orderNumber', 'Order ID', fromSheet(form.orderNumber, original?.orderNumber, original?.orderNumberConfidence))}
          {field('deliverTo', 'Deliver to', fromSheet(form.deliverTo, original?.deliverTo, original?.deliverToConfidence), { multiline: true })}
          {field('totalProducts', 'Total products', fromSheet(form.totalProducts, original?.totalProducts, original?.totalProductsConfidence), { numeric: true })}
          {field('deliveryInstructions', 'Delivery instructions', fromSheet(form.deliveryInstructions, original?.deliveryInstructions, original?.deliveryInstructionsConfidence), { multiline: true })}

          <fieldset className="review-field signature-question" disabled={!editable}>
            <legend>Signature present</legend>
            <label>
              <input
                type="radio"
                name="signature"
                checked={form.signaturePresent === true}
                onChange={() => update({ signaturePresent: true })}
              />{' '}
              Yes
            </label>
            <label>
              <input
                type="radio"
                name="signature"
                checked={form.signaturePresent === false}
                onChange={() => update({ signaturePresent: false })}
              />{' '}
              No
            </label>
            <span className="from-sheet">{signatureHint}</span>
          </fieldset>

          {field('exceptionNotes', 'Exceptions / notes', <span className="from-sheet">Optional, e.g. "Left at door" or "Left with neighbor".</span>, { multiline: true })}
        </div>
      </div>

      {error && (
        <div role="alert" className="alert-box">
          <p>
            <strong>{error.message}</strong>
          </p>
          {error.reasons.length > 0 && (
            <ul>
              {error.reasons.map((r) => (
                <li key={r}>{r}</li>
              ))}
            </ul>
          )}
        </div>
      )}
      {saved && (
        <p role="status" className="notice">
          Saved.
        </p>
      )}

      {editable && asking === null && (
        <div className="review-actions">
          <button
            type="button"
            className="primary"
            disabled={busy}
            onClick={() => run(() => approveReview(confirmationId, edit()), (d) => onDone(`Approved order ${order(d)}.`))}
          >
            Approve
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={() =>
              run(() => saveReview(confirmationId, edit()), (d) => {
                setDetail(d)
                setForm(formFrom(d))
                setSaved(true)
              })
            }
          >
            Save changes
          </button>
          <button type="button" className="secondary" disabled={busy} onClick={() => { setAsking('manager'); setReason('') }}>
            Send to manager
          </button>
          <button type="button" className="danger" disabled={busy} onClick={() => { setAsking('reject'); setReason('') }}>
            Reject
          </button>
        </div>
      )}

      {editable && asking === 'reject' && (
        <div className="ask-box">
          <label htmlFor="reject-reason">Reason (optional)</label>
          <textarea id="reject-reason" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />
          <div className="review-actions">
            <button
              type="button"
              className="danger"
              disabled={busy}
              onClick={() =>
                run(() => rejectReview(confirmationId, detail.version, reason.trim()), (d) =>
                  onDone(order(d) ? `Rejected the sheet for order ${order(d)}.` : 'Rejected the sheet.'),
                )
              }
            >
              Reject this sheet
            </button>
            <button type="button" className="secondary" onClick={() => setAsking(null)}>
              Cancel
            </button>
          </div>
        </div>
      )}

      {editable && asking === 'manager' && (
        <div className="ask-box">
          <label htmlFor="manager-note">Note for the manager</label>
          <textarea id="manager-note" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />
          <div className="review-actions">
            <button
              type="button"
              className="primary"
              disabled={busy || reason.trim() === ''}
              onClick={() =>
                run(() => sendToManager(confirmationId, detail.version, reason.trim()), (d) =>
                  onDone(order(d) ? `Sent order ${order(d)} to a manager.` : 'Sent the sheet to a manager.'),
                )
              }
            >
              Send it
            </button>
            <button type="button" className="secondary" onClick={() => setAsking(null)}>
              Cancel
            </button>
          </div>
        </div>
      )}

      <div className="page-actions">
        <button type="button" onClick={onBack}>
          <span aria-hidden="true">←</span> Back
        </button>
      </div>
    </section>
  )
}
