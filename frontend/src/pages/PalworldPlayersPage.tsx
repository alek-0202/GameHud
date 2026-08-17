import type { FormEvent } from 'react'
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import {
  banPalworldPlayer,
  kickPalworldPlayer,
  sendPalworldAnnouncement,
  unbanPalworldPlayer,
} from '../api/palworld'
import { SectionHeader } from '../components/SectionHeader'
import { usePalworldPlayers } from '../hooks/usePalworldPlayers'
import { formatPlayerLimit, getInitials } from '../utils/palworldDisplay'
import { PalworldUnavailableState } from './PalworldLayout'

export function PalworldPlayersPage() {
  const { serverId = 'palworld' } = useParams()
  const playersState = usePalworldPlayers(15000, serverId)
  const [announcement, setAnnouncement] = useState('')
  const [unbanUserId, setUnbanUserId] = useState('')
  const [actionMessage, setActionMessage] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  if (playersState.status === 'loading') {
    return <p className="state-message">Loading Palworld players...</p>
  }

  if (playersState.status !== 'success') {
    return <PalworldUnavailableState message={playersState.message} />
  }

  const players = playersState.players

  async function handleAnnouncementSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setActionMessage(null)
    setActionError(null)
    setIsSubmitting(true)

    try {
      const result = await sendPalworldAnnouncement(announcement, serverId)
      setAnnouncement('')
      setActionMessage(result.message)
    } catch {
      setActionError('Unable to send announcement.')
    } finally {
      setIsSubmitting(false)
    }
  }

  async function runPlayerAction(action: 'kick' | 'ban', userId: string | null, playerName: string) {
    if (userId === null) {
      setActionError('Player user id is unavailable.')
      return
    }

    const expectedConfirmation = `${action.toUpperCase()} ${userId}`
    const confirmation = window.prompt(
      `${action === 'ban' ? 'Ban' : 'Kick'} ${playerName}. Type ${expectedConfirmation} to confirm.`,
    )

    if (confirmation === null) {
      return
    }

    setActionMessage(null)
    setActionError(null)
    setIsSubmitting(true)

    try {
      const result = action === 'ban'
        ? await banPalworldPlayer(serverId, userId, confirmation, null)
        : await kickPalworldPlayer(serverId, userId, confirmation, null)

      setActionMessage(result.message)
    } catch {
      setActionError(`Unable to ${action} player.`)
    } finally {
      setIsSubmitting(false)
    }
  }

  async function handleUnbanSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const normalizedUserId = unbanUserId.trim()
    const expectedConfirmation = `UNBAN ${normalizedUserId}`
    const confirmation = window.prompt(`Type ${expectedConfirmation} to confirm.`)

    if (confirmation === null) {
      return
    }

    setActionMessage(null)
    setActionError(null)
    setIsSubmitting(true)

    try {
      const result = await unbanPalworldPlayer(serverId, normalizedUserId, confirmation)
      setUnbanUserId('')
      setActionMessage(result.message)
    } catch {
      setActionError('Unable to unban player.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="page-section">
      <SectionHeader
        eyebrow="Players"
        title="Players Online"
        description="Current online players reported by the Palworld REST API."
        aside={formatPlayerLimit(players.onlineCount, players.maxPlayers)}
      />

      <section className="details-block" aria-labelledby="palworld-announcement">
        <SectionHeader
          titleId="palworld-announcement"
          title="Announcement"
          description="Send a plain-text message through the Palworld REST API."
        />
        <form className="settings-actions" onSubmit={handleAnnouncementSubmit}>
          <label className="setting-field">
            <span>Message</span>
            <input
              maxLength={200}
              onChange={(event) => setAnnouncement(event.target.value)}
              placeholder="Restart in 10 minutes"
              value={announcement}
            />
          </label>
          <button
            className="primary-button"
            disabled={isSubmitting || announcement.trim().length === 0}
            type="submit"
          >
            Send Announcement
          </button>
        </form>
      </section>

      {actionMessage !== null && (
        <p className="state-message state-message-success">{actionMessage}</p>
      )}
      {actionError !== null && (
        <p className="state-message state-message-error">{actionError}</p>
      )}

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
                <div>
                  <dt>User ID</dt>
                  <dd>{player.userId ?? '-'}</dd>
                </div>
              </dl>
              <div className="inline-actions">
                <button
                  className="secondary-button"
                  disabled={isSubmitting || player.userId === null}
                  onClick={() => void runPlayerAction('kick', player.userId, player.name)}
                  type="button"
                >
                  Kick
                </button>
                <button
                  className="danger-button"
                  disabled={isSubmitting || player.userId === null}
                  onClick={() => void runPlayerAction('ban', player.userId, player.name)}
                  type="button"
                >
                  Ban
                </button>
              </div>
            </article>
          ))}
        </div>
      )}

      <section className="details-block" aria-labelledby="palworld-unban">
        <SectionHeader
          titleId="palworld-unban"
          title="Unban"
          description="Use a known Palworld user id. GamesHud does not show a banned players list without a reliable source."
        />
        <form className="settings-actions" onSubmit={handleUnbanSubmit}>
          <label className="setting-field">
            <span>User ID</span>
            <input
              maxLength={128}
              onChange={(event) => setUnbanUserId(event.target.value)}
              value={unbanUserId}
            />
          </label>
          <button
            className="secondary-button"
            disabled={isSubmitting || unbanUserId.trim().length === 0}
            type="submit"
          >
            Unban
          </button>
        </form>
      </section>
    </div>
  )
}
