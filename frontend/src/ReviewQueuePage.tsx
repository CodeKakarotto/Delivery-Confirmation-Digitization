import { useEffect, useState } from 'react'
import { getWaitingSheets, type ScanSummary } from './api'
import { ExceptionsCell, OrderIdCell } from './ScanCells'
import { formatTime } from './scanGroups'

type Props = {
  onBack: () => void
  onOpen: (confirmationId: string) => void
  /** Message from the last action, e.g. "Approved order MF-10001." */
  notice?: string | null
}

const show = (value: string | number | null) => (value === null || value === '' ? '—' : String(value))

/** "Check & approve": sheets waiting for a person to check them, oldest first. */
export function ReviewQueuePage({ onBack, onOpen, notice }: Props) {
  const [sheets, setSheets] = useState<ScanSummary[] | null | undefined>(undefined)

  useEffect(() => {
    let cancelled = false
    getWaitingSheets().then((list) => {
      if (!cancelled) setSheets(list)
    })
    return () => {
      cancelled = true
    }
  }, [])

  const open = (sheet: ScanSummary) => sheet.confirmationId && onOpen(sheet.confirmationId)

  return (
    <section className="card">
      <h2>Check &amp; approve</h2>
      <p>Click a sheet to compare it with what was read, fix anything wrong, and approve it.</p>

      {notice && (
        <p role="status" className="notice">
          {notice}
        </p>
      )}

      {sheets === undefined && <p>Loading…</p>}
      {sheets === null && <p className="error">Can't load the waiting sheets right now. Please try again in a minute.</p>}
      {sheets && sheets.length === 0 && <p>Nothing is waiting for review.</p>}
      {sheets && sheets.length > 0 && (
        <>
          <p className="muted">
            {sheets.length} {sheets.length === 1 ? 'sheet is' : 'sheets are'} waiting, oldest first.
          </p>
          <div className="table-scroll">
            <table className="data-table clickable-rows" aria-label="Sheets waiting for review">
              <thead>
                <tr>
                  <th>#</th>
                  <th>Order ID</th>
                  <th>Zone</th>
                  <th>Deliver to</th>
                  <th className="exceptions-cell">Exceptions / notes</th>
                  <th>Uploaded</th>
                </tr>
              </thead>
              <tbody>
                {sheets.map((sheet, index) => (
                  <tr key={sheet.scanId} onClick={() => open(sheet)}>
                    <td className="nowrap">
                      <button
                        type="button"
                        className="link-button"
                        aria-label={`Check sheet ${index + 1}`}
                        onClick={(e) => {
                          e.stopPropagation()
                          open(sheet)
                        }}
                      >
                        {index + 1}
                      </button>
                    </td>
                    <OrderIdCell scan={sheet} />
                    <td className="nowrap">{show(sheet.zone)}</td>
                    <td>{show(sheet.deliverTo)}</td>
                    <ExceptionsCell scan={sheet} />
                    <td className="nowrap">
                      {new Date(sheet.uploadedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}{' '}
                      {formatTime(sheet.uploadedAt)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      <div className="page-actions">
        <button type="button" onClick={onBack}>
          <span aria-hidden="true">←</span> Back
        </button>
      </div>
    </section>
  )
}
