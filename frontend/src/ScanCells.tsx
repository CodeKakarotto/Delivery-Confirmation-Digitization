import type { ScanSummary } from './api'

/** Order ID is never blank: the ID, "Reading…" while the sheet is read, or a red "Missing". */
export function OrderIdCell({ scan }: { scan: ScanSummary }) {
  if (scan.orderNumber) return <td className="nowrap">{scan.orderNumber}</td>
  if (scan.status === 'Reading')
    return (
      <td className="nowrap">
        <span className="tag-inline tag-wait">Reading…</span>
      </td>
    )
  return (
    <td className="nowrap">
      <span className="tag-inline tag-bad">Missing</span>
    </td>
  )
}

/** Small "Exceptions / notes" cell: one short label per exception, or a dash. */
export function ExceptionsCell({ scan }: { scan: ScanSummary }) {
  const labels = scan.exceptions ?? []
  return (
    <td className="exceptions-cell">
      {labels.length === 0
        ? '—'
        : labels.map((label) => (
            <span key={label} className="chip">
              {label}
            </span>
          ))}
    </td>
  )
}
