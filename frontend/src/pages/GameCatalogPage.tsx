import { useEffect, useRef, useState } from 'react'
import { ListChecks, Network } from 'lucide-react'
import { Link } from 'react-router-dom'
import { fetchGameCatalog, fetchGameCompatibility, fetchGamePortPlan } from '../api/games'
import { SectionHeader } from '../components/SectionHeader'
import type {
  GameCatalogGame,
  GameCompatibilityAssessment,
  GameCompatibilityCheck,
  GamePortPlan,
  GamePortPlanItem,
  NetworkPort,
} from '../types/games'

export function GameCatalogPage() {
  const [catalogState, setCatalogState] = useState<
    | { status: 'loading'; games: GameCatalogGame[]; message?: undefined }
    | { status: 'success'; games: GameCatalogGame[]; message?: undefined }
    | { status: 'error'; games: GameCatalogGame[]; message: string }
  >({ status: 'loading', games: [] })

  useEffect(() => {
    const abortController = new AbortController()

    async function loadCatalog() {
      try {
        const games = await fetchGameCatalog(abortController.signal)
        setCatalogState({ status: 'success', games })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setCatalogState({
          status: 'error',
          games: [],
          message: 'Unable to load the game catalog.',
        })
      }
    }

    void loadCatalog()

    return () => abortController.abort()
  }, [])

  return (
    <section className="page-section" aria-labelledby="game-catalog-title">
      <SectionHeader
        aside={`${catalogState.games.length} known`}
        description="GamesHud product knowledge from registered game definitions. Configured servers stay separate."
        eyebrow="Game catalog"
        title="Game Catalog"
        titleId="game-catalog-title"
      />

      {catalogState.status === 'loading' && (
        <p className="state-message">Loading game catalog...</p>
      )}

      {catalogState.status === 'error' && (
        <p className="state-message state-message-error">{catalogState.message}</p>
      )}

      {catalogState.status === 'success' && catalogState.games.length === 0 && (
        <p className="empty-message">No games are registered in the catalog.</p>
      )}

      {catalogState.games.length > 0 && (
        <div className="game-catalog-grid">
          {catalogState.games.map((game) => (
            <GameCatalogCard game={game} key={game.id} />
          ))}
        </div>
      )}
    </section>
  )
}

interface GameCatalogCardProps {
  game: GameCatalogGame
}

function GameCatalogCard({ game }: GameCatalogCardProps) {
  const visibleCapabilities = game.capabilities.slice(0, 5)
  const hiddenCapabilityCount = Math.max(0, game.capabilities.length - visibleCapabilities.length)
  const abortControllerRef = useRef<AbortController | null>(null)
  const portPlanAbortControllerRef = useRef<AbortController | null>(null)
  const [compatibilityState, setCompatibilityState] = useState<
    | { status: 'idle'; assessment?: undefined; message?: undefined }
    | { status: 'loading'; assessment?: undefined; message?: undefined }
    | { status: 'success'; assessment: GameCompatibilityAssessment; message?: undefined }
    | { status: 'error'; assessment?: undefined; message: string }
  >({ status: 'idle' })
  const [portPlanState, setPortPlanState] = useState<
    | { status: 'idle'; plan?: undefined; message?: undefined }
    | { status: 'loading'; plan?: undefined; message?: undefined }
    | { status: 'success'; plan: GamePortPlan; message?: undefined }
    | { status: 'error'; plan?: undefined; message: string }
  >({ status: 'idle' })

  useEffect(() => {
    return () => {
      abortControllerRef.current?.abort()
      portPlanAbortControllerRef.current?.abort()
    }
  }, [])

  async function checkCompatibility() {
    abortControllerRef.current?.abort()
    const abortController = new AbortController()
    abortControllerRef.current = abortController
    setCompatibilityState({ status: 'loading' })

    try {
      const assessment = await fetchGameCompatibility(game.id, abortController.signal)
      setCompatibilityState({ status: 'success', assessment })
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return
      }

      setCompatibilityState({
        status: 'error',
        message: 'Unable to check host requirements.',
      })
    }
  }

  async function checkPortPlan() {
    portPlanAbortControllerRef.current?.abort()
    const abortController = new AbortController()
    portPlanAbortControllerRef.current = abortController
    setPortPlanState({ status: 'loading' })

    try {
      const plan = await fetchGamePortPlan(game.id, abortController.signal)
      setPortPlanState({ status: 'success', plan })
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return
      }

      setPortPlanState({
        status: 'error',
        message: 'Unable to check network requirements.',
      })
    }
  }

  return (
    <article className="game-catalog-card">
      <div className="game-catalog-card-header">
        <div className="game-catalog-identity">
          <span className="game-catalog-icon" aria-hidden="true">
            {formatIconKey(game.branding.iconKey)}
          </span>
          <div>
            <span className="section-eyebrow">Management available</span>
            <h3>{game.displayName}</h3>
          </div>
        </div>
        <span className="status-badge status-badge-info">
          <span aria-hidden="true" />
          Known game
        </span>
      </div>

      <p>{game.description}</p>

      <dl className="game-catalog-meta">
        <div>
          <dt>Runtime</dt>
          <dd>{formatList(game.supportedRuntimes.map(formatRuntime))}</dd>
        </div>
        <div>
          <dt>Capabilities</dt>
          <dd>{game.capabilities.length} capabilities</dd>
        </div>
      </dl>

      <div className="game-catalog-capabilities" aria-label={`${game.displayName} capabilities`}>
        {visibleCapabilities.map((capability) => (
          <span className="game-catalog-chip" key={capability}>
            {formatCapability(capability)}
          </span>
        ))}
        {hiddenCapabilityCount > 0 && (
          <span className="game-catalog-chip game-catalog-chip-muted">
            +{hiddenCapabilityCount} more
          </span>
        )}
      </div>

      <div className="server-card-actions">
        <button
          className="secondary-button"
          disabled={compatibilityState.status === 'loading'}
          type="button"
          onClick={() => {
            void checkCompatibility()
          }}
        >
          <ListChecks size={16} strokeWidth={2.2} />
          {getCompatibilityButtonLabel(compatibilityState.status)}
        </button>
        <button
          className="secondary-button"
          disabled={portPlanState.status === 'loading'}
          type="button"
          onClick={() => {
            void checkPortPlan()
          }}
        >
          <Network size={16} strokeWidth={2.2} />
          {getPortPlanButtonLabel(portPlanState.status)}
        </button>
        <Link className="secondary-button" to="/servers">
          Manage Existing Servers
        </Link>
      </div>

      {compatibilityState.status === 'loading' && (
        <p className="game-compatibility-message">Checking host requirements...</p>
      )}

      {compatibilityState.status === 'error' && (
        <p className="game-compatibility-message game-compatibility-message-error">
          {compatibilityState.message}
        </p>
      )}

      {compatibilityState.status === 'success' && (
        <GameCompatibilityPanel assessment={compatibilityState.assessment} />
      )}

      {portPlanState.status === 'loading' && (
        <p className="game-compatibility-message">Checking network requirements...</p>
      )}

      {portPlanState.status === 'error' && (
        <p className="game-compatibility-message game-compatibility-message-error">
          {portPlanState.message}
        </p>
      )}

      {portPlanState.status === 'success' && (
        <GamePortPlanPanel plan={portPlanState.plan} />
      )}
    </article>
  )
}

interface GameCompatibilityPanelProps {
  assessment: GameCompatibilityAssessment
}

function GameCompatibilityPanel({ assessment }: GameCompatibilityPanelProps) {
  return (
    <div className="game-compatibility-panel" aria-live="polite">
      <div className="game-compatibility-summary">
        <div>
          <span className="section-eyebrow">Host fit</span>
          <strong>{formatCompatibilityHeadline(assessment.status)}</strong>
        </div>
        <StatusPill status={assessment.status} />
      </div>

      <div className="game-compatibility-checks">
        {assessment.checks.map((check) => (
          <CompatibilityCheckRow check={check} key={check.id} />
        ))}
      </div>
    </div>
  )
}

interface CompatibilityCheckRowProps {
  check: GameCompatibilityCheck
}

function CompatibilityCheckRow({ check }: CompatibilityCheckRowProps) {
  return (
    <div className="game-compatibility-check">
      <StatusPill status={check.status} />
      <div>
        <strong>{check.label}</strong>
        <dl>
          <div>
            <dt>Required</dt>
            <dd>{check.required}</dd>
          </div>
          <div>
            <dt>Detected</dt>
            <dd>{check.detected}</dd>
          </div>
        </dl>
        <p>{check.message}</p>
      </div>
    </div>
  )
}

interface GamePortPlanPanelProps {
  plan: GamePortPlan
}

function GamePortPlanPanel({ plan }: GamePortPlanPanelProps) {
  return (
    <div className="game-port-panel" aria-live="polite">
      <div className="game-compatibility-summary">
        <div>
          <span className="section-eyebrow">Network requirements</span>
          <strong>{formatPortPlanHeadline(plan.status)}</strong>
        </div>
        <StatusPill status={plan.status} />
      </div>

      <div className="game-port-list">
        {plan.ports.map((port) => (
          <GamePortRow port={port} key={port.definitionId} />
        ))}
      </div>
      <p>{plan.message}</p>
    </div>
  )
}

interface GamePortRowProps {
  port: GamePortPlanItem
}

function GamePortRow({ port }: GamePortRowProps) {
  const suggestedPort = port.allocation.allocatedPort

  return (
    <div className="game-port-row">
      <div>
        <strong>{port.label}</strong>
        <span>{formatExposure(port.exposure)}</span>
      </div>
      <dl>
        <div>
          <dt>Default</dt>
          <dd>{formatNetworkPort(port.availability)}</dd>
        </div>
        <div>
          <dt>Status</dt>
          <dd>{formatStatus(port.availability.status)}</dd>
        </div>
        <div>
          <dt>Suggested</dt>
          <dd>{suggestedPort ? formatNetworkPort(suggestedPort) : 'None'}</dd>
        </div>
      </dl>
      <StatusPill status={port.availability.status} />
    </div>
  )
}

interface StatusPillProps {
  status: string
}

function StatusPill({ status }: StatusPillProps) {
  return (
    <span className={`status-badge ${getStatusClassName(status)}`}>
      <span aria-hidden="true" />
      {formatStatus(status)}
    </span>
  )
}

function formatIconKey(iconKey: string) {
  const normalized = iconKey.trim()

  return normalized.length === 0
    ? 'GH'
    : normalized.slice(0, 2).toUpperCase()
}

function formatRuntime(runtime: string) {
  if (runtime === 'docker') {
    return 'Docker'
  }

  return formatIdentifier(runtime)
}

function formatCapability(capability: string) {
  const labels: Record<string, string> = {
    backups: 'Backups',
    logs: 'Logs',
    mods: 'Mods',
    overview: 'Overview',
    'player-management': 'Player management',
    players: 'Players',
    settings: 'Settings',
    update: 'Updates',
  }

  return labels[capability] ?? formatIdentifier(capability)
}

function formatIdentifier(value: string) {
  return value
    .split('-')
    .filter((part) => part.length > 0)
    .map((part) => `${part[0].toUpperCase()}${part.slice(1)}`)
    .join(' ')
}

function formatList(values: string[]) {
  return values.length === 0 ? 'None declared' : values.join(', ')
}

function getCompatibilityButtonLabel(status: 'idle' | 'loading' | 'success' | 'error') {
  if (status === 'loading') {
    return 'Checking...'
  }

  return status === 'success' ? 'Recheck Requirements' : 'Check Requirements'
}

function getPortPlanButtonLabel(status: 'idle' | 'loading' | 'success' | 'error') {
  if (status === 'loading') {
    return 'Checking...'
  }

  return status === 'success' ? 'Recheck Network' : 'Check Network'
}

function formatCompatibilityHeadline(status: string) {
  const labels: Record<string, string> = {
    compatible: 'Ready to host',
    compatible_with_warnings: 'Ready with warnings',
    incompatible: 'Host requirements need attention',
    unknown: 'Compatibility unknown',
  }

  return labels[status] ?? formatStatus(status)
}

function formatPortPlanHeadline(status: string) {
  const labels: Record<string, string> = {
    conflict: 'Network needs attention',
    ready: 'Ports look available',
    ready_with_alternatives: 'Alternatives suggested',
    unknown: 'Network requirements unknown',
  }

  return labels[status] ?? formatStatus(status)
}

function getStatusClassName(status: string) {
  if (status === 'available'
    || status === 'compatible'
    || status === 'passed'
    || status === 'ready') {
    return 'status-badge-success'
  }

  if (status === 'compatible_with_warnings'
    || status === 'ready_with_alternatives'
    || status === 'warning') {
    return 'status-badge-warning'
  }

  if (status === 'conflict'
    || status === 'failed'
    || status === 'in_use'
    || status === 'incompatible') {
    return 'status-badge-danger'
  }

  if (status === 'unknown') {
    return 'status-badge-muted'
  }

  return 'status-badge-info'
}

function formatStatus(status: string) {
  const labels: Record<string, string> = {
    allocated: 'Allocated',
    available: 'Available',
    compatible: 'Compatible',
    compatible_with_warnings: 'Warnings',
    conflict: 'Conflict',
    failed: 'Failed',
    in_use: 'In use',
    incompatible: 'Incompatible',
    passed: 'Passed',
    ready: 'Ready',
    ready_with_alternatives: 'Alternatives',
    unknown: 'Unknown',
    warning: 'Warning',
  }

  return labels[status] ?? formatIdentifier(status)
}

function formatExposure(exposure: string) {
  const labels: Record<string, string> = {
    internal: 'Internal',
    public: 'Public',
  }

  return labels[exposure] ?? formatIdentifier(exposure)
}

function formatNetworkPort(port: NetworkPort | { port: number; protocol: string }) {
  const number = 'number' in port ? port.number : port.port

  return `${number} ${port.protocol.toUpperCase()}`
}
