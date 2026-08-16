import { useEffect, useState } from 'react'
import { ContainerLifecycleActions } from '../components/ContainerLifecycleActions'
import { MetricProgressCard } from '../components/MetricProgressCard'
import { MiniMetricChart } from '../components/MiniMetricChart'
import { SectionHeader } from '../components/SectionHeader'
import { StatusBadge } from '../components/StatusBadge'
import { useContainerDetails } from '../hooks/useContainerDetails'
import { usePalworldMetrics } from '../hooks/usePalworldMetrics'
import { usePalworldOverview } from '../hooks/usePalworldOverview'
import { usePalworldUpdate } from '../hooks/usePalworldUpdate'
import type { ContainerDetails } from '../types/container'
import type { MetricsHistoryWindow } from '../types/metrics'
import type { PalworldUpdateStatus } from '../types/palworld'
import { formatDuration, formatPlayerLimit, getInitials } from '../utils/palworldDisplay'
import { toFriendlyState } from '../utils/containerStatus'
import { formatBytePair, formatPercent, toPercent } from '../utils/metricsDisplay'
import { PalworldUnavailableState } from './PalworldLayout'

const updateConfirmation = 'UPDATE PALWORLD SERVER'

export function PalworldOverviewPage() {
  const overviewState = usePalworldOverview()
  const [historyWindow, setHistoryWindow] = useState<MetricsHistoryWindow>(1)
  const metricsState = usePalworldMetrics(historyWindow)
  const updateState = usePalworldUpdate()
  const containerName = overviewState.status === 'success' ? overviewState.overview.containerName : null
  const detailsState = useContainerDetails(containerName)
  const [updatedContainer, setUpdatedContainer] = useState<ContainerDetails | null>(null)
  const [copyFeedback, setCopyFeedback] = useState<string | null>(null)
  const [showUpdateModal, setShowUpdateModal] = useState(false)
  const [updateConfirmationText, setUpdateConfirmationText] = useState('')
  const [updateError, setUpdateError] = useState<string | null>(null)
  const visibleContainer = updatedContainer ?? detailsState.container

  useEffect(() => {
    if (detailsState.status === 'success') {
      setUpdatedContainer(detailsState.container)
    }
  }, [detailsState])

  if (overviewState.status === 'loading') {
    return <p className="state-message">Loading Palworld overview...</p>
  }

  if (overviewState.status !== 'success') {
    return <PalworldUnavailableState message={overviewState.message} />
  }

  const overview = overviewState.overview

  return (
    <div className="page-section">
      <SectionHeader
        eyebrow="Overview"
        title={overview.displayName}
        description={overview.description ?? 'Friendly summary using Palworld REST API and Docker status.'}
      />

      <div className="server-overview-grid">
        <OverviewCard label="Players" value={formatPlayerLimit(overview.onlinePlayers, overview.maxPlayers)} />
        <OverviewCard label="Uptime" value={formatDuration(overview.uptimeSeconds)} />
        <OverviewCard label="Server Version" value={overview.version ?? 'Unknown'} />
        <div className="server-overview-card">
          <span className="section-eyebrow">Server Health</span>
          <StatusBadge state={overview.healthLabel} />
          {overview.restApiMessage && <p>{overview.restApiMessage}</p>}
        </div>
      </div>

      <PalworldUpdatePanel
        isChecking={updateState.status === 'checking'}
        isUpdating={updateState.isUpdating}
        onCheck={() => {
          setUpdateError(null)
          void updateState.check()
        }}
        onOpenUpdate={() => {
          setUpdateConfirmationText('')
          setUpdateError(null)
          setShowUpdateModal(true)
        }}
        update={updateState.update}
        message={updateState.status === 'success' ? updateState.message : null}
      />

      {updateState.lastResult !== null && (
        <p className="state-message state-message-success">
          Update finished. Health check: {updateState.lastResult.healthCheckStatus}.
        </p>
      )}

      {updateError !== null && (
        <p className="state-message state-message-error">{updateError}</p>
      )}

      {(updateState.status === 'unavailable' || updateState.status === 'error') && (
        <p className="state-message state-message-error">{updateState.message}</p>
      )}

      {metricsState.status === 'success' && (
        <section className="details-block" aria-labelledby="palworld-resource-usage">
          <SectionHeader
            titleId="palworld-resource-usage"
            title="Resource Usage"
            description="Container CPU, memory and short Palworld history."
          />
          <div className="server-overview-grid resource-usage-grid">
            <MetricProgressCard
              label="CPU"
              percent={metricsState.metrics.cpuPercent}
              value={formatPercent(metricsState.metrics.cpuPercent)}
            />
            <MetricProgressCard
              label="RAM"
              percent={metricsState.metrics.memoryPercent}
              value={formatBytePair(
                metricsState.metrics.memoryUsageBytes,
                metricsState.metrics.memoryLimitBytes,
              )}
            />
          </div>
          <div className="chart-toolbar">
            {[1, 6, 24].map((hours) => (
              <button
                aria-pressed={historyWindow === hours}
                className={historyWindow === hours ? 'chart-range-active' : ''}
                key={hours}
                type="button"
                onClick={() => setHistoryWindow(hours as MetricsHistoryWindow)}
              >
                {hours}h
              </button>
            ))}
          </div>
          <div className="metric-chart-grid">
            <MiniMetricChart
              label="CPU"
              max={100}
              unit="%"
              values={metricsState.metrics.history.map((point) => point.palworldCpuPercent)}
            />
            <MiniMetricChart
              label="RAM"
              max={100}
              unit="%"
              values={metricsState.metrics.history.map((point) => (
                toPercent(point.palworldMemoryUsageBytes, point.palworldMemoryLimitBytes)
              ))}
            />
            <MiniMetricChart
              label="Players"
              max={metricsState.metrics.maxPlayers ?? undefined}
              values={metricsState.metrics.history.map((point) => point.playersOnline)}
            />
          </div>
        </section>
      )}

      {metricsState.status !== 'success' && metricsState.status !== 'loading' && (
        <p className="state-message state-message-error">{metricsState.message}</p>
      )}

      <section className="details-block" aria-labelledby="palworld-online-players">
        <SectionHeader titleId="palworld-online-players" title="Players Online" />
        {overview.players.length === 0 ? (
          <p className="empty-message">No players online</p>
        ) : (
          <div className="players-list compact-players-list">
            {overview.players.map((player) => (
              <article className="player-row" key={player.publicId ?? player.name}>
                <div className="player-avatar" aria-hidden="true">
                  {getInitials(player.name)}
                </div>
                <div>
                  <strong>{player.name}</strong>
                  <span>
                    {player.ping === null ? 'Ping unavailable' : `${Math.round(player.ping)} ms`}
                  </span>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="details-block" aria-labelledby="palworld-connection">
        <SectionHeader titleId="palworld-connection" title="Connection" />
        <div className="summary-panel connection-panel">
          <div>
            <span className="section-eyebrow">Server address</span>
            <strong>{overview.connectionAddress ?? 'Not configured'}</strong>
          </div>
          <button
            className="secondary-button"
            disabled={overview.connectionAddress === null}
            type="button"
            onClick={() => {
              void copyConnectionAddress(overview.connectionAddress, setCopyFeedback)
            }}
          >
            Copy
          </button>
        </div>
        {copyFeedback && <p className="state-message state-message-success">{copyFeedback}</p>}
      </section>

      <section className="details-block" aria-labelledby="palworld-controls">
        <SectionHeader
          titleId="palworld-controls"
          title="Server Controls"
          description={`Docker state: ${toFriendlyState(visibleContainer?.state ?? overview.containerState)}.`}
        />

        {detailsState.status === 'loading' && (
          <p className="state-message">Loading server operations...</p>
        )}

        {detailsState.status !== 'success'
          && detailsState.status !== 'loading'
          && detailsState.status !== 'idle' && (
          <p className="state-message state-message-error">{detailsState.message}</p>
        )}

        {detailsState.status === 'success' && (
          <ContainerLifecycleActions
            container={visibleContainer ?? detailsState.container}
            onContainerUpdated={setUpdatedContainer}
          />
        )}
      </section>

      {showUpdateModal && updateState.update !== null && (
        <div className="modal-backdrop" role="presentation">
          <form
            aria-labelledby="palworld-update-modal-title"
            className="modal-panel palworld-update-modal"
            onSubmit={(event) => {
              event.preventDefault()
              setUpdateError(null)
              void updateState.updateServer(updateConfirmationText)
                .then(() => {
                  setShowUpdateModal(false)
                  setUpdateConfirmationText('')
                })
                .catch(() => {
                  setUpdateError('Unable to update Palworld server.')
                })
            }}
          >
            <h3 id="palworld-update-modal-title">Update Palworld server</h3>
            <p>
              This will save the world, create a pre-update backup, stop Palworld,
              let the container update on boot, start it again and run a health check.
            </p>
            <dl className="details-grid compact-details-grid">
              <dt>Players online</dt>
              <dd>{overview.onlinePlayers}</dd>
              <dt>Downtime</dt>
              <dd>Expected during stop, update and startup.</dd>
              <dt>Installed</dt>
              <dd>{updateState.update.installedVersion ?? 'Unknown'}</dd>
              <dt>Available</dt>
              <dd>{updateState.update.availableVersion ?? 'Unknown'}</dd>
              <dt>Backup</dt>
              <dd>Automatic pre-update backup required.</dd>
            </dl>
            <label className="form-field">
              Confirmation
              <input
                autoFocus
                onChange={(event) => setUpdateConfirmationText(event.target.value)}
                placeholder={updateConfirmation}
                type="text"
                value={updateConfirmationText}
              />
            </label>
            <div className="modal-actions">
              <button
                className="secondary-button"
                disabled={updateState.isUpdating}
                onClick={() => setShowUpdateModal(false)}
                type="button"
              >
                Cancel
              </button>
              <button
                className="danger-button"
                disabled={updateState.isUpdating || updateConfirmationText !== updateConfirmation}
                type="submit"
              >
                {updateState.isUpdating ? 'Updating...' : 'Update Server'}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  )
}

interface OverviewCardProps {
  label: string
  value: string
}

function OverviewCard({ label, value }: OverviewCardProps) {
  return (
    <div className="server-overview-card">
      <span className="section-eyebrow">{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

interface PalworldUpdatePanelProps {
  update: PalworldUpdateStatus | null
  message: string | null
  isChecking: boolean
  isUpdating: boolean
  onCheck: () => void
  onOpenUpdate: () => void
}

function PalworldUpdatePanel({
  update,
  message,
  isChecking,
  isUpdating,
  onCheck,
  onOpenUpdate,
}: PalworldUpdatePanelProps) {
  const updateAvailable = update?.updateStatus === 'update-available'

  return (
    <section className="details-block" aria-labelledby="palworld-update-status">
      <SectionHeader
        titleId="palworld-update-status"
        title="Server Version"
        description="Manual update checks use SteamCMD metadata from the configured Palworld container."
      />
      <div className="summary-panel palworld-update-panel">
        <dl className="details-grid compact-details-grid">
          <dt>Installed</dt>
          <dd>{update?.installedVersion ?? 'Unknown'}</dd>
          <dt>Latest/status</dt>
          <dd>
            <StatusBadge state={formatUpdateStatus(update?.updateStatus ?? 'unknown')} />
            <span className="table-subtext">
              {update?.availableVersion ?? update?.message ?? 'Not checked yet'}
            </span>
          </dd>
          <dt>Last Checked</dt>
          <dd>{update === null ? 'Never' : formatDate(update.lastCheckedAt)}</dd>
        </dl>
        <div className="palworld-update-actions">
          <button
            className="secondary-button"
            disabled={isChecking || isUpdating}
            onClick={onCheck}
            type="button"
          >
            {isChecking ? 'Checking...' : 'Check for Updates'}
          </button>
          {updateAvailable && (
            <button
              className="danger-button"
              disabled={isUpdating}
              onClick={onOpenUpdate}
              type="button"
            >
              Update Server
            </button>
          )}
        </div>
      </div>
      {message !== null && <p className="state-message state-message-success">{message}</p>}
    </section>
  )
}

function formatUpdateStatus(status: string) {
  if (status === 'update-available') {
    return 'Update available'
  }

  if (status === 'up-to-date') {
    return 'Up to date'
  }

  if (status === 'check-unavailable') {
    return 'Check unavailable'
  }

  return 'Unknown'
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

async function copyConnectionAddress(
  connectionAddress: string | null,
  setCopyFeedback: (message: string | null) => void,
) {
  if (connectionAddress === null) {
    return
  }

  await navigator.clipboard.writeText(connectionAddress)
  setCopyFeedback('Connection address copied.')
}
