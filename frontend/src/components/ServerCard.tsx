import { Link } from 'react-router-dom'
import type { Container } from '../types/container'
import type { PalworldConfig, PalworldOverview } from '../types/palworld'
import { formatDuration, formatPlayerLimit, getInitials } from '../utils/palworldDisplay'
import { getPalworldServerName } from '../utils/palworldSettings'
import { StatusBadge } from './StatusBadge'

interface ServerCardProps {
  serverId?: string
  displayName?: string
  config: PalworldConfig | null
  container: Container | null
  overview?: PalworldOverview | null
}

export function ServerCard({
  serverId = 'palworld',
  displayName,
  config,
  container,
  overview = null,
}: ServerCardProps) {
  const serverName = overview?.displayName || displayName || getPalworldServerName(config)
  const state = overview?.healthLabel || container?.state || 'Unknown'
  const onlinePlayers = overview?.onlinePlayers ?? 0
  const maxPlayers = overview?.maxPlayers ?? null
  const visiblePlayers = overview?.players.slice(0, 4) ?? []
  const remainingPlayers = Math.max(0, onlinePlayers - visiblePlayers.length)

  return (
    <article className="server-card">
      <div className="server-card-header">
        <div>
          <span className="section-eyebrow">Game server</span>
          <h3>{serverName}</h3>
        </div>
        <StatusBadge state={state} />
      </div>

      <dl className="server-meta">
        <div>
          <dt>Players Online</dt>
          <dd>{formatPlayerLimit(onlinePlayers, maxPlayers)}</dd>
        </div>
        <div>
          <dt>Uptime</dt>
          <dd>{formatDuration(overview?.uptimeSeconds ?? null)}</dd>
        </div>
        <div>
          <dt>Version</dt>
          <dd>{overview?.version ?? 'Unknown'}</dd>
        </div>
        <div>
          <dt>Players</dt>
          <dd>
            {visiblePlayers.length === 0 ? (
              'No players online'
            ) : (
              <span className="inline-player-list">
                {visiblePlayers.map((player) => (
                  <span className="player-chip" key={player.publicId ?? player.name}>
                    <span>{getInitials(player.name)}</span>
                    {player.name}
                  </span>
                ))}
                {remainingPlayers > 0 && <span>+{remainingPlayers}</span>}
              </span>
            )}
          </dd>
        </div>
      </dl>

      <div className="server-card-actions">
        <Link className="secondary-button" to={`/servers/${encodeURIComponent(serverId)}`}>
          Manage Server
        </Link>
      </div>
    </article>
  )
}
