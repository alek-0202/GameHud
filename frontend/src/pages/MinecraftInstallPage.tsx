import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  createMinecraftManagedServer,
  fetchManagedProvisioning,
  type ManagedProvisioning,
} from '../api/managedGameServers'
import { SectionHeader } from '../components/SectionHeader'

const ramChoices = [6144, 8192, 10240, 12288]
const terminalStatuses = new Set(['completed', 'failed', 'requires_inspection'])

export function MinecraftInstallPage() {
  const [name, setName] = useState('Lunchtime Minecraft')
  const [ramMiB, setRamMiB] = useState(6144)
  const [port, setPort] = useState(25565)
  const [eula, setEula] = useState(false)
  const [serverId, setServerId] = useState<string | null>(null)
  const [provisioning, setProvisioning] = useState<ManagedProvisioning | null>(null)
  const [state, setState] = useState<'idle' | 'submitting' | 'running' | 'error'>('idle')
  const [message, setMessage] = useState<string | null>(null)
  const retryIdentity = useRef<{ payload: string; key: string } | null>(null)

  const payload = useMemo(() => JSON.stringify({ name: name.trim(), ramMiB, port, eula }), [name, ramMiB, port, eula])

  useEffect(() => {
    if (!serverId || state !== 'running') return
    const abortController = new AbortController()
    let timeout: number | undefined

    async function poll() {
      try {
        const current = await fetchManagedProvisioning(serverId!, abortController.signal)
        setProvisioning(current)
        if (!terminalStatuses.has(current.status.toLowerCase())) {
          timeout = window.setTimeout(() => { void poll() }, 2000)
        }
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setState('error')
        setMessage(error instanceof Error ? error.message : 'Installation progress could not be loaded.')
      }
    }

    void poll()
    return () => {
      abortController.abort()
      if (timeout !== undefined) window.clearTimeout(timeout)
    }
  }, [serverId, state])

  async function install(event: FormEvent) {
    event.preventDefault()
    if (!name.trim() || port < 1024 || port > 65535 || !eula) {
      setState('error')
      setMessage('Enter a name and valid port, then explicitly accept the Minecraft EULA.')
      return
    }

    const identity = retryIdentity.current?.payload === payload
      ? retryIdentity.current
      : { payload, key: crypto.randomUUID() }
    retryIdentity.current = identity
    setState('submitting')
    setMessage(null)
    try {
      const created = await createMinecraftManagedServer(
        name.trim(), { ramMiB, port, eulaAccepted: eula }, identity.key,
      )
      setServerId(created.gameServerId)
      setState('running')
    } catch (error) {
      setState('error')
      setMessage(error instanceof Error ? error.message : 'Installation could not be scheduled.')
    }
  }

  return (
    <section className="page-section" aria-labelledby="minecraft-install-title">
      <SectionHeader
        eyebrow="Managed installation"
        titleId="minecraft-install-title"
        title="Minecraft Java · Lunchtime"
        description="Install the fixed Lunchtime: Cozy Create and Cooking v2.6.0-r2 server pack through the durable Managed pipeline."
        aside="Minecraft 1.20.1 · Forge 47.4.29 · Java 17"
      />

      <form className="settings-form minecraft-install-form" onSubmit={install}>
        <div className="settings-grid">
          <label className="form-field">
            <span>Modpack</span>
            <input value="Lunchtime: Cozy Create and Cooking v2.6.0-r2" disabled />
          </label>
          <label className="form-field">
            <span>Server name</span>
            <input value={name} maxLength={80} onChange={(event) => setName(event.target.value)} required />
          </label>
          <label className="form-field">
            <span>Java memory</span>
            <select value={ramMiB} onChange={(event) => setRamMiB(Number(event.target.value))}>
              {ramChoices.map((value) => <option key={value} value={value}>{value / 1024} GiB heap</option>)}
            </select>
            <small>The container receives 1 GiB additional overhead.</small>
          </label>
          <label className="form-field">
            <span>Public TCP port</span>
            <input type="number" min={1024} max={65535} value={port} onChange={(event) => setPort(Number(event.target.value))} required />
          </label>
        </div>

        <label className="inline-check-field">
          <input type="checkbox" checked={eula} onChange={(event) => setEula(event.target.checked)} />
          <span>I explicitly accept the <a href="https://aka.ms/MinecraftEULA" target="_blank" rel="noreferrer">Minecraft EULA</a>.</span>
        </label>
        <p className="state-message">Docker running is not game readiness. Completion waits for the container healthcheck to report healthy.</p>
        <button className="primary-button" type="submit" disabled={state === 'submitting' || state === 'running'}>
          {state === 'submitting' ? 'Scheduling…' : state === 'running' ? 'Installation scheduled' : 'Install'}
        </button>
      </form>

      {message && <p className="state-message state-message-error" role="alert">{message}</p>}
      {provisioning && <ProvisioningProgress provisioning={provisioning} />}
      {serverId && <p><Link className="secondary-button" to={`/managed-servers/${encodeURIComponent(serverId)}`}>Open managed server</Link></p>}
    </section>
  )
}

function ProvisioningProgress({ provisioning }: { provisioning: ManagedProvisioning }) {
  return (
    <section className="settings-card" aria-live="polite" aria-label="Installation progress">
      <h2>Installation {formatStatus(provisioning.status)}</h2>
      <p>Pipeline <code>{provisioning.pipelineVersion}</code> · current step <code>{provisioning.currentStep}</code></p>
      <ol className="minecraft-step-list">
        {provisioning.steps.map((step) => (
          <li key={step.stepId}><strong>{formatStatus(step.stepId)}</strong> — {formatStatus(step.status)}</li>
        ))}
      </ol>
      {provisioning.failure && <p className="state-message state-message-error" role="alert">{provisioning.failure.message}</p>}
    </section>
  )
}

function formatStatus(value: string) {
  return value.replaceAll('_', ' ').replace(/^./, (letter) => letter.toUpperCase())
}
