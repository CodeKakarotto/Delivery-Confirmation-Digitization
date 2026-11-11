type Props = { onOpenUpload: () => void; onOpenHistory: () => void }

/** Home screen: one big button per job. More buttons arrive with later steps. */
export function HomePage({ onOpenUpload, onOpenHistory }: Props) {
  return (
    <section className="card">
      <h2>What would you like to do?</h2>
      <div className="tiles">
        <button type="button" className="tile" onClick={onOpenUpload}>
          Scan delivery sheets
        </button>
        <button type="button" className="tile" onClick={onOpenHistory}>
          See all scans
        </button>
      </div>
    </section>
  )
}
