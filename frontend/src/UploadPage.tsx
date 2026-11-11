import { useState } from 'react'
import { uploadScan, type UploadResult } from './api'

type Props = { onBack: () => void }

/** "Scan delivery sheets": choose pictures of the paper sheets and upload them. */
export function UploadPage({ onBack }: Props) {
  const [files, setFiles] = useState<File[]>([])
  const [results, setResults] = useState<UploadResult[]>([])
  const [busy, setBusy] = useState(false)

  async function handleUpload() {
    setBusy(true)
    setResults([])
    // One file per request: each gets its own result, and requests stay
    // well under Cloudflare's size limit.
    for (const file of files) {
      const result = await uploadScan(file)
      setResults((previous) => [...previous, result])
    }
    setBusy(false)
    setFiles([])
  }

  return (
    <section className="card">
      <h2>Scan delivery sheets</h2>
      <p>
        Choose the scanned or photographed delivery sheets. Use JPEG or PNG pictures, one sheet
        per file, up to 10 MB each.
      </p>

      <div className="file-row">
        <label htmlFor="scan-files" className="file-picker">
          Choose files
        </label>
        <input
          id="scan-files"
          type="file"
          multiple
          accept="image/jpeg,image/png"
          onChange={(e) => {
            setFiles(Array.from(e.target.files ?? []))
            setResults([])
          }}
        />
        <p>
          {files.length === 0
            ? 'No files chosen yet.'
            : `${files.length} ${files.length === 1 ? 'file' : 'files'} chosen`}
        </p>
      </div>

      <button
        type="button"
        className="primary"
        disabled={files.length === 0 || busy}
        onClick={handleUpload}
      >
        Upload
      </button>
      {busy && <p className="uploading">Uploading… please wait.</p>}

      {results.length > 0 && (
        <ul className="results">
          {results.map((result, index) => (
            <li
              key={index}
              className={!result.saved ? 'result-bad' : result.duplicate ? 'result-warn' : 'result-ok'}
            >
              <span aria-hidden="true">{!result.saved ? '✗' : result.duplicate ? '⚠' : '✓'}</span>{' '}
              <strong>{result.fileName}</strong>
              <br />
              <span>{result.message}</span>
            </li>
          ))}
        </ul>
      )}

      <div className="page-actions">
        <button type="button" onClick={onBack}>
          <span aria-hidden="true">←</span> Back
        </button>
      </div>
    </section>
  )
}
