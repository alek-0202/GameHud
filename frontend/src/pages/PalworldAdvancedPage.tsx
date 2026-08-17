import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { fetchPalworldMods } from '../api/palworld'
import { SectionHeader } from '../components/SectionHeader'
import { StatusBadge } from '../components/StatusBadge'
import { useContainerDetails } from '../hooks/useContainerDetails'
import { usePalworldConfig } from '../hooks/usePalworldConfig'
import type { PalworldMods } from '../types/palworld'
import { PalworldUnavailableState } from './PalworldLayout'

export function PalworldAdvancedPage() {
  const { serverId = 'palworld' } = useParams()
  const palworldState = usePalworldConfig(0, serverId)
  const config = palworldState.status === 'success' ? palworldState.config : null
  const detailsState = useContainerDetails(config?.containerName ?? null)
  const [modsState, setModsState] = useState<
    | { status: 'loading'; mods: null; message?: undefined }
    | { status: 'success'; mods: PalworldMods; message?: undefined }
    | { status: 'error'; mods: null; message: string }
  >({ status: 'loading', mods: null })

  useEffect(() => {
    const abortController = new AbortController()

    async function loadMods() {
      try {
        const mods = await fetchPalworldMods(serverId, abortController.signal)
        setModsState({ status: 'success', mods })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setModsState({
          status: 'error',
          mods: null,
          message: 'Unable to load Palworld mod inventory.',
        })
      }
    }

    void loadMods()

    return () => abortController.abort()
  }, [serverId])

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

      <section className="details-block" aria-labelledby="palworld-mods">
        <SectionHeader
          titleId="palworld-mods"
          title="Mods"
          description={modsState.status === 'success'
            ? modsState.mods.message
            : 'Local inventory only. Installation and enable/disable are reserved for a safer workflow.'}
        />
        {modsState.status === 'loading' && (
          <p className="state-message">Loading mod inventory...</p>
        )}
        {modsState.status === 'error' && (
          <p className="state-message state-message-error">{modsState.message}</p>
        )}
        {modsState.status === 'success' && (
          modsState.mods.detectedMods.length === 0 ? (
            <p className="empty-message">No local mod files detected in known Palworld paths.</p>
          ) : (
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Relative path</th>
                    <th>Size</th>
                  </tr>
                </thead>
                <tbody>
                  {modsState.mods.detectedMods.map((mod) => (
                    <tr key={mod.relativePath}>
                      <td>{mod.name}</td>
                      <td>{mod.relativePath}</td>
                      <td>{Math.round(mod.sizeBytes / 1024)} KB</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        )}
      </section>
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
