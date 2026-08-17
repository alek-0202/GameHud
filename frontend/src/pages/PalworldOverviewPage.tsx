import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import {
  Activity,
  Clock,
  Copy,
  Gauge,
  Globe2,
  HeartPulse,
  Network,
  RefreshCw,
  Server,
  ShieldCheck,
  Users,
  Wifi,
  Zap,
} from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
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
import type { PalworldOverview, PalworldUpdateStatus } from '../types/palworld'
import { formatDuration, formatPlayerLimit, getInitials } from '../utils/palworldDisplay'
import { toFriendlyState } from '../utils/containerStatus'
import { formatBytePair, formatPercent, toPercent } from '../utils/metricsDisplay'
import { PalworldUnavailableState } from './PalworldLayout'

const updateConfirmation = 'UPDATE PALWORLD SERVER'

export function PalworldOverviewPage() {
  const { serverId = 'palworld' } = useParams()
  const overviewState = usePalworldOverview(15000, serverId)
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
  const serverState = toFriendlyState(visibleContainer?.state ?? overview.containerState)
  const playersPath = `/servers/${encodeURIComponent(serverId)}/players`

  return (
    <div className="page-section palworld-overview-page">
      <PalworldServerHero overview={overview} serverState={serverState} />

      {detailsState.status === 'loading' && (
        <section className="details-block lifecycle-block lifecycle-block-overview">
          <h3>Quick Actions</h3>
          <p className="section-description">Loading server operations...</p>
        </section>
      )}

      {detailsState.status !== 'success'
        && detailsState.status !== 'loading'
        && detailsState.status !== 'idle' && (
        <section className="details-block lifecycle-block lifecycle-block-overview">
          <h3>Quick Actions</h3>
          <p className="state-message state-message-error">{detailsState.message}</p>
        </section>
      )}

      {detailsState.status === 'success' && (
        <ContainerLifecycleActions
          container={visibleContainer ?? detailsState.container}
          description={`Docker state: ${serverState}. Start is available when the server is offline; stop and restart keep the existing confirmations.`}
          onContainerUpdated={setUpdatedContainer}
          title="Quick Actions"
          variant="overview"
        />
      )}

      <section className="details-block palworld-status-panel" aria-labelledby="palworld-server-status">
        <SectionHeader
          titleId="palworld-server-status"
          title="Server Status"
          description="Live Palworld REST data paired with the managed Docker container state."
        />
        <div className="palworld-status-grid">
          <StatusTile
            detail={`${overview.onlinePlayers} currently online`}
            icon={<Users aria-hidden="true" size={18} />}
            label="Players"
            tone="success"
            value={formatPlayerLimit(overview.onlinePlayers, overview.maxPlayers)}
          />
          <StatusTile
            detail={overview.containerStatus || overview.containerName}
            icon={<HeartPulse aria-hidden="true" size={18} />}
            label="Health"
            value={<StatusBadge state={overview.healthLabel} />}
          />
          <StatusTile
            detail="Current session"
            icon={<Clock aria-hidden="true" size={18} />}
            label="Uptime"
            value={formatDuration(overview.uptimeSeconds)}
          />
          <StatusTile
            detail={overview.restApiMessage ?? 'Palworld REST API is reachable'}
            icon={<Wifi aria-hidden="true" size={18} />}
            label="Connection"
            tone={overview.restApiAvailable ? 'success' : 'warning'}
            value={overview.restApiAvailable ? 'REST online' : 'REST unavailable'}
          />
          <StatusTile
            detail="Installed server build"
            icon={<ShieldCheck aria-hidden="true" size={18} />}
            label="Version"
            value={overview.version ?? 'Unknown'}
          />
          <StatusTile
            detail="World progression"
            icon={<Globe2 aria-hidden="true" size={18} />}
            label="In-game Days"
            value={formatNullableNumber(overview.inGameDays)}
          />
        </div>
      </section>

      <div className="palworld-overview-split">
        <section className="details-block palworld-players-panel" aria-labelledby="palworld-online-players">
          <div className="palworld-section-heading">
            <div>
              <span className="section-eyebrow">Players Online</span>
              <h3 id="palworld-online-players">{formatPlayerLimit(overview.onlinePlayers, overview.maxPlayers)}</h3>
            </div>
            <Link className="ghost-button" to={playersPath}>
              View all players
            </Link>
          </div>
          {overview.players.length === 0 ? (
            <p className="empty-message">No players online</p>
          ) : (
            <div className="players-list palworld-players-list">
              {overview.players.slice(0, 5).map((player) => (
                <article className="player-row palworld-player-row" key={player.publicId ?? player.name}>
                  <div className="player-avatar" aria-hidden="true">
                    {getInitials(player.name)}
                  </div>
                  <div>
                    <strong>{player.name}</strong>
                    <span>{player.publicId ?? player.accountName ?? 'Player'}</span>
                  </div>
                  <span className="palworld-player-ping">
                    {player.ping === null ? 'Ping unavailable' : `${Math.round(player.ping)} ms`}
                  </span>
                </article>
              ))}
              {overview.players.length > 5 && (
                <p className="table-subtext">+{overview.players.length - 5} more online</p>
              )}
            </div>
          )}
        </section>

        <section className="details-block palworld-connection-card" aria-labelledby="palworld-connection">
          <div className="palworld-section-heading">
            <div>
              <span className="section-eyebrow">Connection</span>
              <h3 id="palworld-connection">Join Details</h3>
            </div>
            <Network aria-hidden="true" size={20} />
          </div>
          <dl className="details-grid compact-details-grid">
            <dt>Server address</dt>
            <dd>{overview.connectionAddress ?? 'Not configured'}</dd>
            <dt>REST API</dt>
            <dd>{overview.restApiAvailable ? 'Available' : 'Unavailable'}</dd>
            <dt>Docker container</dt>
            <dd>{overview.containerName}</dd>
          </dl>
          <button
            className="secondary-button"
            disabled={overview.connectionAddress === null}
            type="button"
            onClick={() => {
              void copyConnectionAddress(overview.connectionAddress, setCopyFeedback)
            }}
          >
            <Copy aria-hidden="true" size={16} />
            Copy Address
          </button>
          {copyFeedback && <p className="state-message state-message-success">{copyFeedback}</p>}
        </section>
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
        <section className="details-block palworld-resource-panel" aria-labelledby="palworld-resource-usage">
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

      {metricsState.status === 'loading' && (
        <p className="state-message">Loading resource usage...</p>
      )}

      {metricsState.status !== 'success' && metricsState.status !== 'loading' && (
        <p className="state-message state-message-error">{metricsState.message}</p>
      )}

      <section className="details-block palworld-world-panel" aria-labelledby="palworld-world-info">
        <SectionHeader
          titleId="palworld-world-info"
          title="World Information"
          description="Operational details reported by the Palworld server when available."
        />
        <div className="palworld-world-grid">
          <WorldMetric
            icon={<Globe2 aria-hidden="true" size={18} />}
            label="In-game days"
            value={formatNullableNumber(overview.inGameDays)}
          />
          <WorldMetric
            icon={<Gauge aria-hidden="true" size={18} />}
            label="Server FPS"
            value={formatFps(overview.serverFps)}
          />
          <WorldMetric
            icon={<Activity aria-hidden="true" size={18} />}
            label="Frame time"
            value={formatFrameTime(overview.serverFrameTime)}
          />
          <WorldMetric
            icon={<Server aria-hidden="true" size={18} />}
            label="Base camps"
            value={formatNullableNumber(overview.baseCampCount)}
          />
        </div>
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

interface PalworldServerHeroProps {
  overview: PalworldOverview
  serverState: string
}

function PalworldServerHero({ overview, serverState }: PalworldServerHeroProps) {
  return (
    <section className="palworld-overview-hero" aria-labelledby="palworld-overview-title">
      <div className="palworld-overview-hero-main">
        <div className="palworld-overview-hero-icon" aria-hidden="true">
          <Zap size={22} />
        </div>
        <div>
          <span className="section-eyebrow">Palworld Server</span>
          <h2 id="palworld-overview-title">{overview.displayName}</h2>
          <p>{overview.description ?? overview.serverName}</p>
        </div>
      </div>
      <div className="palworld-overview-hero-aside">
        <StatusBadge state={overview.healthLabel} />
        <dl className="palworld-hero-stats">
          <div>
            <dt>Players</dt>
            <dd>{formatPlayerLimit(overview.onlinePlayers, overview.maxPlayers)}</dd>
          </div>
          <div>
            <dt>Uptime</dt>
            <dd>{formatDuration(overview.uptimeSeconds)}</dd>
          </div>
          <div>
            <dt>Version</dt>
            <dd>{overview.version ?? 'Unknown'}</dd>
          </div>
          <div>
            <dt>State</dt>
            <dd>{serverState}</dd>
          </div>
        </dl>
      </div>
    </section>
  )
}

interface StatusTileProps {
  label: string
  value: ReactNode
  detail: string
  icon: ReactNode
  tone?: 'neutral' | 'success' | 'warning'
}

function StatusTile({
  label,
  value,
  detail,
  icon,
  tone = 'neutral',
}: StatusTileProps) {
  return (
    <article className={`palworld-status-tile palworld-status-tile-${tone}`}>
      <div className="palworld-status-tile-heading">
        <span className="palworld-status-icon">{icon}</span>
        <span>{label}</span>
      </div>
      <strong>{value}</strong>
      <p>{detail}</p>
    </article>
  )
}

interface WorldMetricProps {
  label: string
  value: string
  icon: ReactNode
}

function WorldMetric({ label, value, icon }: WorldMetricProps) {
  return (
    <article className="palworld-world-metric">
      <span className="palworld-status-icon">{icon}</span>
      <div>
        <span>{label}</span>
        <strong>{value}</strong>
      </div>
    </article>
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
  const updateStatus = update?.updateStatus ?? 'unknown'
  const statusLabel = formatUpdateStatus(updateStatus)

  return (
    <section
      className={`details-block palworld-version-panel palworld-version-panel-${getUpdateTone(updateStatus)}`}
      aria-labelledby="palworld-update-status"
    >
      <div className="palworld-version-header">
        <div>
          <span className="section-eyebrow">Server Version</span>
          <h3 id="palworld-update-status">Installed and Available Build</h3>
          <p>Manual update checks use SteamCMD metadata from the configured Palworld container.</p>
        </div>
        <StatusBadge state={statusLabel} />
      </div>
      <div className="palworld-version-body">
        <div className="palworld-version-grid">
          <VersionField label="Installed" value={update?.installedVersion ?? 'Unknown'} />
          <VersionField
            label="Latest/status"
            value={update?.availableVersion ?? statusLabel}
            detail={update?.message ?? 'Not checked yet'}
          />
          <VersionField label="Last Checked" value={update === null ? 'Never' : formatDate(update.lastCheckedAt)} />
        </div>
        <div className="palworld-update-actions">
          <button
            className="secondary-button"
            disabled={isChecking || isUpdating}
            onClick={onCheck}
            type="button"
          >
            <RefreshCw aria-hidden="true" size={16} />
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

interface VersionFieldProps {
  label: string
  value: string
  detail?: string
}

function VersionField({ label, value, detail }: VersionFieldProps) {
  return (
    <div className="palworld-version-field">
      <span>{label}</span>
      <strong>{value}</strong>
      {detail && <p>{detail}</p>}
    </div>
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

function getUpdateTone(status: string) {
  if (status === 'update-available') {
    return 'warning'
  }

  if (status === 'up-to-date') {
    return 'success'
  }

  if (status === 'check-unavailable') {
    return 'warning'
  }

  return 'neutral'
}

function formatNullableNumber(value: number | null) {
  return value === null ? 'Unknown' : new Intl.NumberFormat().format(value)
}

function formatFps(value: number | null) {
  return value === null ? 'Unknown' : `${Math.round(value)} FPS`
}

function formatFrameTime(value: number | null) {
  return value === null ? 'Unknown' : `${value.toFixed(1)} ms`
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

  if (!navigator.clipboard) {
    setCopyFeedback('Clipboard is unavailable in this browser.')
    return
  }

  try {
    await navigator.clipboard.writeText(connectionAddress)
    setCopyFeedback('Connection address copied.')
  } catch {
    setCopyFeedback('Unable to copy connection address.')
  }
}
