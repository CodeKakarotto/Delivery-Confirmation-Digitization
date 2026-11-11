import { useEffect } from 'react'
import { scanImageUrl, type ScanSummary } from './api'
import { formatTime } from './scanGroups'

type Props = { scan: ScanSummary; onClose: () => void }

const REVIEW_STYLE: Record<string, string> = {
  Confirmed: 'review-ok',
  Denied: 'review-bad',
  Voided: 'review-bad',
  'Sent to manager': 'review-wait',
}

const READ_TEXT = {
  Read: 'Read automatically',
  Failed: "Couldn't be read automatically",
  Reading: 'Still being read…',
} as const

const show = (value: string | number | null) => (value === null || value === '' ? '—' : String(value))

/** Pop-up showing the scanned paper sheet itself, with what was read and the review result. */
export function ScanViewer({ scan, onClose }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  const title = scan.orderNumber ? `Sheet for order ${scan.orderNumber}` : `Sheet ${scan.fileName}`
  const review = scan.reviewStatus ?? 'Waiting for review'
  const exceptions = scan.exceptions ?? []

  return (
    <div className="viewer-backdrop" onClick={onClose}>
      <div
        className="viewer"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="viewer-header">
          <h2>{title}</h2>
          <button type="button" onClick={onClose} autoFocus>
            Close
          </button>
        </div>

        <p className="viewer-status">
          <span className={`review-badge ${REVIEW_STYLE[review] ?? 'review-neutral'}`}>{review}</span>
          <span className="muted">
            {' '}
            · Recorded {new Date(scan.uploadedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}{' '}
            at {formatTime(scan.uploadedAt)} ·{' '}
          </span>
          <span className={scan.status === 'Failed' ? 'result-text-bad' : 'muted'}>{READ_TEXT[scan.status]}</span>
        </p>

        <div className="viewer-body">
          <img className="viewer-image" src={scanImageUrl(scan.scanId)} alt={`Scanned delivery sheet ${scan.fileName}`} />

          <dl className="viewer-fields">
            <dt>Zone</dt>
            <dd>{show(scan.zone)}</dd>
            <dt>Order ID</dt>
            <dd>{scan.orderNumber ?? <span className="tag-inline tag-bad">Missing</span>}</dd>
            <dt>Deliver to</dt>
            <dd>{show(scan.deliverTo)}</dd>
            <dt>Total products</dt>
            <dd>{show(scan.totalProducts)}</dd>
            <dt>Delivery instructions</dt>
            <dd>{show(scan.deliveryInstructions)}</dd>
            <dt>Exceptions / notes</dt>
            <dd>
              {exceptions.length === 0
                ? '—'
                : exceptions.map((label) => (
                    <span key={label} className="chip">
                      {label}
                    </span>
                  ))}
            </dd>
            <dt>File</dt>
            <dd>{scan.fileName}</dd>
          </dl>
        </div>
      </div>
    </div>
  )
}
