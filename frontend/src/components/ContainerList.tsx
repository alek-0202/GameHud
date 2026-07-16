import type { Container } from '../types/container'

interface ContainerListProps {
  containers: Container[]
}

export function ContainerList({ containers }: ContainerListProps) {
  return (
    <div className="container-list">
      {containers.map((container) => (
        <article className="container-card" key={container.id}>
          <h3>{container.name || container.id}</h3>
          <dl className="container-detail">
            <dt>Name</dt>
            <dd>{container.name || 'Unnamed container'}</dd>
            <dt>Image</dt>
            <dd>{container.image || 'Unknown image'}</dd>
            <dt>State</dt>
            <dd>{container.state || 'Unknown state'}</dd>
            <dt>Status</dt>
            <dd>{container.status || 'Unknown status'}</dd>
          </dl>
        </article>
      ))}
    </div>
  )
}
