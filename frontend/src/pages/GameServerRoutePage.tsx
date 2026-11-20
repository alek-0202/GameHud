import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import {
  fetchGameServer,
  GameServersApiRequestError,
} from '../api/gameServers'
import type { GameServer } from '../types/gameServers'
import { ExternalGameServerPage } from './ExternalGameServerPage'
import { PalworldLayout } from './PalworldLayout'

type GameServerState =
  | { status: 'loading'; server: null; message?: undefined }
  | { status: 'success'; server: GameServer; message?: undefined }
  | { status: 'not-found'; server: null; message: string }
  | { status: 'error'; server: null; message: string }

export function GameServerRoutePage() {
  const { serverId } = useParams()
  const [state, setState] = useState<GameServerState>({ status: 'loading', server: null })

  useEffect(() => {
    if (!serverId) {
      setState({
        status: 'not-found',
        server: null,
        message: 'Game server was not found.',
      })
      return
    }

    const resolvedServerId = serverId
    const abortController = new AbortController()

    async function loadServer() {
      try {
        setState({ status: 'loading', server: null })
        const server = await fetchGameServer(resolvedServerId, abortController.signal)
        setState({ status: 'success', server })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        if (error instanceof GameServersApiRequestError && error.status === 404) {
          setState({
            status: 'not-found',
            server: null,
            message: 'Game server was not found.',
          })
          return
        }

        setState({
          status: 'error',
          server: null,
          message: 'Unable to load the configured game server.',
        })
      }
    }

    void loadServer()

    return () => abortController.abort()
  }, [serverId])

  if (state.status === 'loading') {
    return <p className="state-message">Loading game server...</p>
  }

  if (state.status !== 'success') {
    return <p className="state-message state-message-error">{state.message}</p>
  }

  if (state.server.gameType.toLowerCase() === 'palworld') {
    return <PalworldLayout />
  }

  if (!isSupportedExternalServer(state.server)) {
    return (
      <p className="state-message state-message-error">
        This server does not expose a supported LegacyExternal Docker runtime.
      </p>
    )
  }

  return <ExternalGameServerPage server={state.server} />
}

export function isSupportedExternalServer(server: GameServer) {
  return server.runtimeType.toLowerCase() === 'docker'
    && server.installationType === 'legacyExternal'
    && server.containerName.trim().length > 0
}
