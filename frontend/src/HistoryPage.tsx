import { useEffect, useState } from 'react'
import { getScanHistory, type ScanSummary } from './api'
import { ExceptionsCell, OrderIdCell } from './ScanCells'
import { ScanViewer } from './ScanViewer'
import { describeCounts, formatTime, groupByWeekAndDay } from './scanGroups'

type Props = { onBack: () => void }

const RESULT = {
  Read: { text: '✓ Read', className: 'result-text-ok' },
  Failed: { text: "✗ Couldn't read", className: 'result-text-bad' },
  Reading: { text: '⏳ Still reading', className: 'result-text-wait' },
} as const

/** "All scans": every scanned sheet from the last 12 weeks, by week, then by day. */
export function HistoryPage({ onBack }: Props) {
  const [scans, setScans] = useState<ScanSummary[] | null | undefined>(undefined)
  const [openWeek, setOpenWeek] = useState<string | null>(null)
  const [openDay, setOpenDay] = useState<string | null>(null)
  const [viewing, setViewing] = useState<ScanSummary | null>(null)

  useEffect(() => {
    let cancelled = false
    getScanHistory().then((list) => {
      if (!cancelled) setScans(list)
    })
    return () => {
      cancelled = true
    }
  }, [])

  const weeks = scans ? groupByWeekAndDay(scans) : []

  return (
    <section className="card">
      <h2>All scans</h2>
      <p>Click a week to see its days, then click a day to see each scan and the time it was recorded.</p>

      {scans === undefined && <p>Loading…</p>}
      {scans === null && <p className="error">Can't load the scans right now. Please try again in a minute.</p>}
      {scans && scans.length === 0 && <p>Nothing has been scanned in the last 12 weeks.</p>}

      <div className="groups">
        {weeks.map((week) => {
          const weekOpen = openWeek === week.key
          return (
            <div key={week.key} className="group">
              <button
                type="button"
                className="group-toggle"
                aria-expanded={weekOpen}
                onClick={() => {
                  setOpenWeek(weekOpen ? null : week.key)
                  setOpenDay(null)
                }}
              >
                <span className="group-title">{week.label}</span>
                <span className="group-counts">{describeCounts(week.counts)}</span>
              </button>

              {weekOpen && (
                <div className="group-body">
                  {week.days.map((day) => {
                    const dayOpen = openDay === day.key
                    return (
                      <div key={day.key} className="group">
                        <button
                          type="button"
                          className="group-toggle day-toggle"
                          aria-expanded={dayOpen}
                          onClick={() => setOpenDay(dayOpen ? null : day.key)}
                        >
                          <span className="group-title">{day.label}</span>
                          <span className="group-counts">{describeCounts(day.counts)}</span>
                        </button>

                        {dayOpen && (
                          <div className="table-scroll">
                            <table className="data-table clickable-rows" aria-label={`Scans on ${day.label}`}>
                              <thead>
                                <tr>
                                  <th>Time recorded</th>
                                  <th>Result</th>
                                  <th>Order ID</th>
                                  <th>File</th>
                                  <th className="exceptions-cell">Exceptions / notes</th>
                                </tr>
                              </thead>
                              <tbody>
                                {day.scans.map((scan) => (
                                  <tr key={scan.scanId} onClick={() => setViewing(scan)}>
                                    <td className="nowrap">
                                      <button
                                        type="button"
                                        className="link-button"
                                        aria-label={`View sheet recorded at ${formatTime(scan.uploadedAt)}`}
                                        onClick={(e) => {
                                          e.stopPropagation()
                                          setViewing(scan)
                                        }}
                                      >
                                        {formatTime(scan.uploadedAt)}
                                      </button>
                                    </td>
                                    <td className={`nowrap ${RESULT[scan.status].className}`}>{RESULT[scan.status].text}</td>
                                    <OrderIdCell scan={scan} />
                                    <td>{scan.fileName}</td>
                                    <ExceptionsCell scan={scan} />
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        )}
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
          )
        })}
      </div>

      {viewing && <ScanViewer scan={viewing} onClose={() => setViewing(null)} />}

      <div className="page-actions">
        <button type="button" onClick={onBack}>
          <span aria-hidden="true">←</span> Back
        </button>
      </div>
    </section>
  )
}
