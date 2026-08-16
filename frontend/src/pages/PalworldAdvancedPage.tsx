import { SectionHeader } from '../components/SectionHeader'
import { StatusBadge } from '../components/StatusBadge'
import { useContainerDetails } from '../hooks/useContainerDetails'
import { usePalworldConfig } from '../hooks/usePalworldConfig'
import { PalworldUnavailableState } from './PalworldLayout'

export function PalworldAdvancedPage() {
  const palworldState = usePalworldConfig()
  const config = palworldState.status === 'success' ? palworldState.config : null
  const detailsState = useContainerDetails(config?.containerName ?? null)

  if (palworldState.status === 'loading' || detailsState.status === 'loading') {
    return <p className="state-message">Loading Palworld technical information...</p>
  }

  if (palworldState.status !== 'success') {
    return <PalworldUnavailableState message={palworldState.message} />
  }

  if (detailsState.status !== 'success') {
    return (
      <PalworldUnavailableState
        message={detailsState.message ?? 'Container details are unavailable.'}
      />
    )
  }

  const container = detailsState.container

  return (
    <div className="page-section">
      <SectionHeader
        eyebrow="Advanced"
        title="Technical information"
        description="Docker-specific details for operators who need infrastructure context."
      />

      <section className="details-block" aria-labelledby="palworld-technical-general">
        <SectionHeader titleId="palworld-technical-general" title="Container" />
        <dl className="details-grid">
          <dt>Name</dt>
          <dd>{container.name || 'Unknown container'}</dd>
          <dt>Image</dt>
          <dd>{container.image || 'Unknown image'}</dd>
          <dt>Docker state</dt>
          <dd><StatusBadge state={container.state || 'Unknown'} /></dd>
          <dt>Status</dt>
          <dd>{container.status || 'Unknown status'}</dd>
        </dl>
      </section>

      <TechnicalTable
        columns={['Private port', 'Public port', 'Type', 'Host IP']}
        emptyMessage="No ports exposed."
        rows={container.ports.map((port) => [
          port.privatePort.toString(),
          port.publicPort?.toString() ?? 'Not published',
          port.type || 'Unknown',
          port.hostIp || 'Not bound',
        ])}
        title="Ports"
      />

      <TechnicalTable
        columns={['Type', 'Source', 'Destination', 'Read-only']}
        emptyMessage="No mounts configured."
        rows={container.mounts.map((mount) => [
          mount.type || 'Unknown',
          mount.source || 'Unknown source',
          mount.destination || 'Unknown destination',
          mount.readOnly ? 'Yes' : 'No',
        ])}
        title="Mounts"
      />

      <TechnicalTable
        columns={['Name', 'IP address', 'Gateway', 'MAC address']}
        emptyMessage="No networks connected."
        rows={container.networks.map((network) => [
          network.name || 'Unknown network',
          network.ipAddress || 'No IP address',
          network.gateway || 'No gateway',
          network.macAddress || 'No MAC address',
        ])}
        title="Networks"
      />
    </div>
  )
}

interface TechnicalTableProps {
  title: string
  columns: string[]
  rows: string[][]
  emptyMessage: string
}

function TechnicalTable({
  title,
  columns,
  rows,
  emptyMessage,
}: TechnicalTableProps) {
  const titleId = `palworld-${title.toLowerCase()}`

  return (
    <section className="details-block" aria-labelledby={titleId}>
      <SectionHeader titleId={titleId} title={title} />
      {rows.length === 0 ? (
        <p className="empty-message">{emptyMessage}</p>
      ) : (
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                {columns.map((column) => (
                  <th key={column}>{column}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((row, rowIndex) => (
                <tr key={row.join('-') || rowIndex}>
                  {row.map((value, columnIndex) => (
                    <td key={`${value}-${columnIndex}`}>{value}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
