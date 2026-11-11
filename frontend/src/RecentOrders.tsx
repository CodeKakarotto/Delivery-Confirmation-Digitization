import { useEffect, useState } from 'react'
import { getRecentScans, type ScanSummary } from './api'
import { ExceptionsCell, OrderIdCell } from './ScanCells'
import { ScanViewer } from './ScanViewer'

type Props = {
  /** Changes after each upload so the list reloads. */
  refreshKey: number
}

const show = (value: string | number | null) => (value === null || value === '' ? '—' : String(value))

/** "Orders": recently saved uploads, with what was read from each sheet. Click one to see the sheet. */
export function RecentOrders({ refreshKey }: Props) {
  const [scans, setScans] = useState<ScanSummary[] | null | undefined>(undefined)
  const [checks, setChecks] = useState(0)
  const [viewing, setViewing] = useState<ScanSummary | null>(null)

  // Load when shown, after each upload, and on each timed re-check.
  useEffect(() => {
    let cancelled = false
    getRecentScans().then((list) => {
      if (!cancelled) setScans(list)
    })
    return () => {
      cancelled = true
    }
  }, [refreshKey, checks])

  // While any sheet is still being read, check again every 3 seconds.
  const stillReading = scans?.some((s) => s.status === 'Reading') ?? false
  useEffect(() => {
    if (!stillReading) return
    const timer = setTimeout(() => setChecks((n) => n + 1), 3000)
    return () => clearTimeout(timer)
  }, [stillReading, scans])

  return (
    <section className="orders-box" aria-labelledby="orders-heading">
      <h3 id="orders-heading">Orders</h3>
      <p className="muted">Recently saved uploads, newest first. Click one to see the scanned sheet.</p>

      {scans === undefined && <p>Loading…</p>}
      {scans === null && <p className="error">Can't load recent uploads right now.</p>}
      {scans && scans.length === 0 && <p>No uploads yet.</p>}
      {scans && scans.length > 0 && (
        <div className="table-scroll">
          <table className="data-table clickable-rows">
            <thead>
              <tr>
                <th>#</th>
                <th>Zone</th>
                <th>Order ID</th>
                <th>Deliver to</th>
                <th>Total products</th>
                <th>Delivery instructions</th>
                <th className="exceptions-cell">Exceptions / notes</th>
              </tr>
            </thead>
            <tbody>
              {scans.map((scan, index) => (
                <tr
                  key={scan.scanId}
                  className={scan.status === 'Failed' ? 'row-failed' : undefined}
                  onClick={() => setViewing(scan)}
                >
                  <td className="nowrap">
                    <button
                      type="button"
                      className="link-button"
                      aria-label={`View sheet ${index + 1}`}
                      onClick={(e) => {
                        e.stopPropagation()
                        setViewing(scan)
                      }}
                    >
                      {index + 1}
                    </button>
                    {scan.status === 'Failed' && <span className="tag tag-bad">Couldn't read</span>}
                  </td>
                  <td className="nowrap">{show(scan.zone)}</td>
                  <OrderIdCell scan={scan} />
                  <td>{show(scan.deliverTo)}</td>
                  <td className="nowrap">{show(scan.totalProducts)}</td>
                  <td>{show(scan.deliveryInstructions)}</td>
                  <ExceptionsCell scan={scan} />
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {viewing && <ScanViewer scan={viewing} onClose={() => setViewing(null)} />}
    </section>
  )
}
