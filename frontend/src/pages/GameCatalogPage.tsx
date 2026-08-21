import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { fetchGameCatalog } from '../api/games'
import { SectionHeader } from '../components/SectionHeader'
import type { GameCatalogGame } from '../types/games'

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
        <Link className="secondary-button" to="/servers">
          Manage Existing Servers
        </Link>
      </div>
    </article>
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
