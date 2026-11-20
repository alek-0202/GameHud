import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { fetchManagedGameServer, fetchManagedProvisioning, type ManagedGameServer, type ManagedProvisioning } from '../api/managedGameServers'
import { SectionHeader } from '../components/SectionHeader'

export function ManagedGameServerPage() {
  const { serverId } = useParams()
  const [server, setServer] = useState<ManagedGameServer | null>(null)
  const [provisioning, setProvisioning] = useState<ManagedProvisioning | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!serverId) return
    const abortController = new AbortController()
    Promise.all([
      fetchManagedGameServer(serverId, abortController.signal),
      fetchManagedProvisioning(serverId, abortController.signal),
    ]).then(([managedServer, operation]) => {
      setServer(managedServer)
      setProvisioning(operation)
    }).catch((reason: unknown) => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      setError(reason instanceof Error ? reason.message : 'Managed server could not be loaded.')
    })
    return () => abortController.abort()
  }, [serverId])

  if (error) return <p className="state-message state-message-error" role="alert">{error}</p>
  if (!server || !provisioning) return <p className="state-message">Loading managed server…</p>

  return (
    <section className="page-section" aria-labelledby="managed-server-title">
      <SectionHeader eyebrow="Managed Minecraft" titleId="managed-server-title" title={server.displayName}
        description="Durable provisioning status. Runtime management becomes available after installation completes."
        aside={server.lifecycleState} />
      <dl className="game-catalog-meta">
        <div><dt>Game</dt><dd>{server.gameId}</dd></div>
        <div><dt>Installation</dt><dd>{server.installationType}</dd></div>
        <div><dt>Pipeline</dt><dd>{provisioning.pipelineVersion}</dd></div>
        <div><dt>Current step</dt><dd>{provisioning.currentStep}</dd></div>
      </dl>
      <p>Docker running alone does not prove Minecraft is ready; readiness requires a healthy game healthcheck.</p>
      {provisioning.failure && <p className="state-message state-message-error">{provisioning.failure.message}</p>}
      <Link className="secondary-button" to="/servers">Configured external servers</Link>
    </section>
  )
}
