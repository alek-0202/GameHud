import { SectionHeader } from '../components/SectionHeader'
import { usePalworldPlayers } from '../hooks/usePalworldPlayers'
import { formatPlayerLimit, getInitials } from '../utils/palworldDisplay'
import { PalworldUnavailableState } from './PalworldLayout'

export function PalworldPlayersPage() {
  const playersState = usePalworldPlayers()

  if (playersState.status === 'loading') {
    return <p className="state-message">Loading Palworld players...</p>
  }

  if (playersState.status !== 'success') {
    return <PalworldUnavailableState message={playersState.message} />
  }

  const players = playersState.players

  return (
    <div className="page-section">
      <SectionHeader
        eyebrow="Players"
        title="Players Online"
        description="Current online players reported by the Palworld REST API."
        aside={formatPlayerLimit(players.onlineCount, players.maxPlayers)}
      />

      {players.players.length === 0 ? (
        <p className="empty-message">No players online</p>
      ) : (
        <div className="players-list">
          {players.players.map((player) => (
            <article className="player-row" key={player.publicId ?? player.name}>
              <div className="player-avatar" aria-hidden="true">
                {getInitials(player.name)}
              </div>
              <div>
                <strong>{player.name}</strong>
                <span>{player.accountName ?? 'Palworld player'}</span>
              </div>
              <dl>
                <div>
                  <dt>Ping</dt>
                  <dd>{player.ping === null ? '-' : `${Math.round(player.ping)} ms`}</dd>
                </div>
                <div>
                  <dt>Level</dt>
                  <dd>{player.level ?? '-'}</dd>
                </div>
                <div>
                  <dt>ID</dt>
                  <dd>{player.publicId ?? '-'}</dd>
                </div>
              </dl>
            </article>
          ))}
        </div>
      )}
    </div>
  )
}
