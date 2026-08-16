import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { resolvePalworldBackupDownloadUrl } from '../api/palworld'
import { SectionHeader } from '../components/SectionHeader'
import { StatusBadge } from '../components/StatusBadge'
import { usePalworldBackups } from '../hooks/usePalworldBackups'
import type { PalworldBackup } from '../types/palworld'
import { formatBytes } from '../utils/metricsDisplay'
import { PalworldUnavailableState } from './PalworldLayout'

const restoreConfirmation = 'RESTORE PALWORLD BACKUP'
const deleteConfirmation = 'DELETE PALWORLD BACKUP'

export function PalworldBackupsPage() {
  const backupsState = usePalworldBackups(30000)
  const [note, setNote] = useState('')
  const [selectedRestore, setSelectedRestore] = useState<PalworldBackup | null>(null)
  const [selectedDelete, setSelectedDelete] = useState<PalworldBackup | null>(null)
  const [restoreText, setRestoreText] = useState('')
  const [deleteText, setDeleteText] = useState('')
  const [actionMessage, setActionMessage] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const summary = backupsState.status === 'success' ? backupsState.summary : null
  const isBusy = backupsState.action !== null

  const latestBackupDate = useMemo(() => (
    summary?.latestBackup === null || summary?.latestBackup === undefined
      ? 'No backups yet'
      : formatDate(summary.latestBackup.createdAt)
  ), [summary])

  async function handleCreateBackup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setActionMessage(null)
    setActionError(null)

    try {
      await backupsState.createBackup(note.trim() || null)
      setNote('')
      setActionMessage('Backup created.')
    } catch {
      setActionError('Unable to create backup.')
    }
  }

  async function handleRestoreBackup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (selectedRestore === null) {
      return
    }

    setActionMessage(null)
    setActionError(null)

    try {
      await backupsState.restoreBackup(selectedRestore.id, restoreText)
      setSelectedRestore(null)
      setRestoreText('')
      setActionMessage('Backup restored.')
    } catch {
      setActionError('Unable to restore backup.')
    }
  }

  async function handleDeleteBackup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (selectedDelete === null) {
      return
    }

    setActionMessage(null)
    setActionError(null)

    try {
      await backupsState.deleteBackup(selectedDelete.id, deleteText)
      setSelectedDelete(null)
      setDeleteText('')
      setActionMessage('Backup deleted.')
    } catch {
      setActionError('Unable to delete backup.')
    }
  }

  if (backupsState.status === 'loading') {
    return <p className="state-message">Loading Palworld backups...</p>
  }

  if (backupsState.status === 'unavailable') {
    return <PalworldUnavailableState message={backupsState.message} />
  }

  if (backupsState.status === 'error' || summary === null) {
    return <PalworldUnavailableState message={backupsState.message ?? 'Unable to load backups.'} />
  }

  return (
    <div className="page-section">
      <SectionHeader
        eyebrow="Backups"
        title="Palworld backups"
        description="Create, restore and prune backups for the configured Palworld managed directory."
      />

      <div className="backup-summary-grid" aria-label="Palworld backup summary">
        <BackupSummaryCard label="Latest Backup" value={latestBackupDate} />
        <BackupSummaryCard
          label="Next Scheduled"
          value={summary.schedule.enabled
            ? formatDate(summary.schedule.nextScheduledAt)
            : 'Disabled'}
        />
        <BackupSummaryCard
          label="Storage Used"
          value={`${formatBytes(summary.storage.totalBytes)} (${summary.storage.backupCount})`}
        />
      </div>

      <section className="details-block" aria-labelledby="palworld-create-backup">
        <SectionHeader
          titleId="palworld-create-backup"
          title="Create backup"
          description="GamesHud asks Palworld to save the world before creating an online backup when REST is available."
        />
        <form className="backup-create-form" onSubmit={handleCreateBackup}>
          <label>
            Note
            <input
              maxLength={256}
              onChange={(event) => setNote(event.target.value)}
              placeholder="Optional backup note"
              type="text"
              value={note}
            />
          </label>
          <button className="primary-button" disabled={isBusy} type="submit">
            {backupsState.action === 'create' ? 'Creating...' : 'Create Backup'}
          </button>
        </form>
        {actionMessage !== null && (
          <p className="state-message state-message-success">{actionMessage}</p>
        )}
        {actionError !== null && (
          <p className="state-message state-message-error">{actionError}</p>
        )}
      </section>

      <section className="details-block" aria-labelledby="palworld-backup-history">
        <SectionHeader
          titleId="palworld-backup-history"
          title="Backup history"
          description="Restore is destructive and always creates a pre-restore backup first."
        />
        {summary.backups.length === 0 ? (
          <p className="empty-message">No backups found.</p>
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Size</th>
                  <th>Type</th>
                  <th>Status</th>
                  <th>Save</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {summary.backups.map((backup) => (
                  <tr key={backup.id}>
                    <td>
                      <strong>{formatDate(backup.createdAt)}</strong>
                      <span className="table-subtext">{backup.note ?? backup.filename}</span>
                    </td>
                    <td>{formatBytes(backup.sizeBytes)}</td>
                    <td>{formatBackupType(backup.type)}</td>
                    <td><StatusBadge state={backup.status} /></td>
                    <td>{formatSaveStatus(backup.worldSaveStatus)}</td>
                    <td>
                      <div className="backup-actions">
                        {backup.downloadUrl !== null && (
                          <a
                            className="secondary-button"
                            href={resolvePalworldBackupDownloadUrl(backup.downloadUrl) ?? undefined}
                          >
                            Download
                          </a>
                        )}
                        <button
                          className="secondary-button"
                          disabled={isBusy}
                          onClick={() => {
                            setSelectedRestore(backup)
                            setRestoreText('')
                          }}
                          type="button"
                        >
                          Restore
                        </button>
                        <button
                          className="danger-button"
                          disabled={isBusy || summary.backups.length <= 1}
                          onClick={() => {
                            setSelectedDelete(backup)
                            setDeleteText('')
                          }}
                          type="button"
                        >
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {selectedRestore !== null && (
        <div className="modal-backdrop" role="presentation">
          <form
            aria-labelledby="restore-backup-title"
            className="modal-panel backup-confirmation-modal"
            onSubmit={handleRestoreBackup}
          >
            <h3 id="restore-backup-title">Restore backup</h3>
            <p>
              Restore will stop Palworld, create a pre-restore backup, replace the
              managed files and start the configured Palworld container again.
            </p>
            <dl className="details-grid compact-details-grid">
              <dt>Backup</dt>
              <dd>{selectedRestore.filename}</dd>
              <dt>Created</dt>
              <dd>{formatDate(selectedRestore.createdAt)}</dd>
            </dl>
            <label>
              Confirmation
              <input
                autoFocus
                onChange={(event) => setRestoreText(event.target.value)}
                placeholder={restoreConfirmation}
                type="text"
                value={restoreText}
              />
            </label>
            <div className="modal-actions">
              <button
                className="secondary-button"
                disabled={isBusy}
                onClick={() => setSelectedRestore(null)}
                type="button"
              >
                Cancel
              </button>
              <button
                className="danger-button"
                disabled={isBusy || restoreText !== restoreConfirmation}
                type="submit"
              >
                {backupsState.action === `restore:${selectedRestore.id}` ? 'Restoring...' : 'Restore'}
              </button>
            </div>
          </form>
        </div>
      )}

      {selectedDelete !== null && (
        <div className="modal-backdrop" role="presentation">
          <form
            aria-labelledby="delete-backup-title"
            className="modal-panel backup-confirmation-modal"
            onSubmit={handleDeleteBackup}
          >
            <h3 id="delete-backup-title">Delete backup</h3>
            <p>This removes the selected backup archive and metadata.</p>
            <dl className="details-grid compact-details-grid">
              <dt>Backup</dt>
              <dd>{selectedDelete.filename}</dd>
              <dt>Created</dt>
              <dd>{formatDate(selectedDelete.createdAt)}</dd>
            </dl>
            <label>
              Confirmation
              <input
                autoFocus
                onChange={(event) => setDeleteText(event.target.value)}
                placeholder={deleteConfirmation}
                type="text"
                value={deleteText}
              />
            </label>
            <div className="modal-actions">
              <button
                className="secondary-button"
                disabled={isBusy}
                onClick={() => setSelectedDelete(null)}
                type="button"
              >
                Cancel
              </button>
              <button
                className="danger-button"
                disabled={isBusy || deleteText !== deleteConfirmation}
                type="submit"
              >
                {backupsState.action === `delete:${selectedDelete.id}` ? 'Deleting...' : 'Delete'}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  )
}

interface BackupSummaryCardProps {
  label: string
  value: string
}

function BackupSummaryCard({ label, value }: BackupSummaryCardProps) {
  return (
    <article className="metric-card">
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  )
}

function formatDate(value: string | null) {
  if (value === null) {
    return 'Not scheduled'
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function formatBackupType(type: string) {
  if (type === 'pre-restore') {
    return 'Pre-restore'
  }

  return type.charAt(0).toUpperCase() + type.slice(1)
}

function formatSaveStatus(status: string | null) {
  if (status === null) {
    return 'Unknown'
  }

  if (status === 'not-requested') {
    return 'Not requested'
  }

  return status.charAt(0).toUpperCase() + status.slice(1)
}
