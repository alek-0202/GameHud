import { useNavigate } from 'react-router-dom'
import { ContainerList } from '../components/ContainerList'
import { SectionHeader } from '../components/SectionHeader'
import { useContainers } from '../hooks/useContainers'

export function ContainersPage() {
  const navigate = useNavigate()
  const containersState = useContainers()
  const containers = containersState.containers

  return (
    <section className="page-section" aria-labelledby="containers-title">
      <SectionHeader
        eyebrow="Docker Core"
        titleId="containers-title"
        title="Docker Containers"
        description="Technical infrastructure inventory, details, logs and manual lifecycle controls."
        aside={`${containers.length} total`}
      />

      {containersState.status === 'loading' && (
        <p className="state-message">Loading containers...</p>
      )}

      {containersState.status === 'error' && (
        <p className="state-message state-message-error">{containersState.message}</p>
      )}

      {containersState.status === 'success' && containers.length === 0 && (
        <p className="state-message">No containers found.</p>
      )}

      {containersState.status === 'success' && containers.length > 0 && (
        <ContainerList
          containers={containers}
          onSelectContainer={(containerId) => {
            navigate(`/containers/${encodeURIComponent(containerId)}`)
          }}
        />
      )}
    </section>
  )
}
