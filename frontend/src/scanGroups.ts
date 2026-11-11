import type { ScanSummary } from './api'

export type Counts = { total: number; read: number; failed: number; reading: number }
export type DayGroup = { key: string; label: string; scans: ScanSummary[]; counts: Counts }
export type WeekGroup = { key: string; label: string; days: DayGroup[]; counts: Counts }

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate())

/** Weeks start on Monday (in the browser's local time). */
const startOfWeek = (d: Date) => {
  const day = startOfDay(d)
  const sinceMonday = (day.getDay() + 6) % 7
  return new Date(day.getFullYear(), day.getMonth(), day.getDate() - sinceMonday)
}

const countOf = (scans: ScanSummary[]): Counts => ({
  total: scans.length,
  read: scans.filter((s) => s.status === 'Read').length,
  failed: scans.filter((s) => s.status === 'Failed').length,
  reading: scans.filter((s) => s.status === 'Reading').length,
})

export const describeCounts = (c: Counts) =>
  `${c.total} ${c.total === 1 ? 'scan' : 'scans'} · ${c.read} read · ${c.failed} failed` +
  (c.reading > 0 ? ` · ${c.reading} still reading` : '')

export const formatTime = (iso: string) =>
  new Date(iso).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' })

/** Groups scans into weeks (newest first), each split into days (newest first). */
export function groupByWeekAndDay(scans: ScanSummary[]): WeekGroup[] {
  const sorted = [...scans].sort((a, b) => b.uploadedAt.localeCompare(a.uploadedAt))
  const weeks = new Map<number, Map<number, ScanSummary[]>>()

  for (const scan of sorted) {
    const when = new Date(scan.uploadedAt)
    const weekKey = startOfWeek(when).getTime()
    const dayKey = startOfDay(when).getTime()
    const days = weeks.get(weekKey) ?? new Map<number, ScanSummary[]>()
    weeks.set(weekKey, days)
    days.set(dayKey, [...(days.get(dayKey) ?? []), scan])
  }

  return [...weeks.entries()]
    .sort(([a], [b]) => b - a)
    .map(([weekKey, days]) => {
      const dayGroups = [...days.entries()]
        .sort(([a], [b]) => b - a)
        .map(([dayKey, dayScans]) => ({
          key: String(dayKey),
          label: new Date(dayKey).toLocaleDateString('en-US', { weekday: 'long', month: 'short', day: 'numeric' }),
          // Within a day: in the order they were recorded.
          scans: [...dayScans].sort((a, b) => a.uploadedAt.localeCompare(b.uploadedAt)),
          counts: countOf(dayScans),
        }))
      return {
        key: String(weekKey),
        label: `Week of ${new Date(weekKey).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}`,
        days: dayGroups,
        counts: countOf(dayGroups.flatMap((d) => d.scans)),
      }
    })
}
