import { useEffect, useState } from 'react'
import { ApiRequestError, fetchContainerLogs } from '../api/containers'
import {
  logTailOptions,
  type ContainerLogEntry,
  type ContainerLogs as ContainerLogsType,
  type LogStream,
  type LogTailOption,
} from '../types/container'

type LogsState =
  | { status: 'loading' }
  | { status: 'success'; logs: ContainerLogsType }
  | { status: 'error' }

interface ContainerLogsProps {
  containerId: string
}

export function ContainerLogs({ containerId }: ContainerLogsProps) {
  const [tail, setTail] = useState<LogTailOption>(200)
  const [stream, setStream] = useState<LogStream>('all')
  const [search, setSearch] = useState('')
  const [timestamps, setTimestamps] = useState(true)
  const [autoRefresh, setAutoRefresh] = useState(false)
  const [paused, setPaused] = useState(false)
  const [refreshToken, setRefreshToken] = useState(0)
  const [state, setState] = useState<LogsState>({ status: 'loading' })

  useEffect(() => {
    const abortController = new AbortController()

    async function loadLogs() {
      try {
        setState({ status: 'loading' })

        const logs = await fetchContainerLogs(
          containerId,
          tail,
          timestamps,
          stream,
          search,
          abortController.signal,
        )

        setState({ status: 'success', logs })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        if (error instanceof ApiRequestError) {
          setState({ status: 'error' })
          return
        }

        setState({ status: 'error' })
      }
    }

    void loadLogs()

    return () => {
      abortController.abort()
    }
  }, [containerId, refreshToken, search, stream, tail, timestamps])

  useEffect(() => {
    if (!autoRefresh || paused) {
      return
    }

    const intervalId = window.setInterval(() => {
      setRefreshToken((value) => value + 1)
    }, 10000)

    return () => {
      window.clearInterval(intervalId)
    }
  }, [autoRefresh, paused])

  const visibleEntries = state.status === 'success'
    ? state.logs.entries.length > 0
      ? state.logs.entries
      : state.logs.lines.map((line): ContainerLogEntry => ({
        message: line,
        stream: 'all',
        severity: resolveSeverity(line),
        timestamp: null,
      }))
    : []

  return (
    <section className="details-block logs-block" aria-labelledby="logs-title">
      <div className="logs-heading">
        <div>
          <h3 id="logs-title">Logs</h3>
          {state.status === 'success' && (
            <span>Retrieved {formatDate(state.logs.retrievedAt)}</span>
          )}
        </div>
        <div className="logs-actions">
          <label>
            Search
            <input
              maxLength={120}
              type="search"
              value={search}
              onChange={(event) => {
                setSearch(event.currentTarget.value)
              }}
            />
          </label>
          <label>
            Stream
            <select
              value={stream}
              onChange={(event) => {
                setStream(event.currentTarget.value as LogStream)
              }}
            >
              <option value="all">All</option>
              <option value="stdout">stdout</option>
              <option value="stderr">stderr</option>
            </select>
          </label>
          <label>
            Lines
            <select
              value={tail}
              onChange={(event) => {
                const nextTail = Number(event.currentTarget.value)

                if (isLogTailOption(nextTail)) {
                  setTail(nextTail)
                }
              }}
            >
              {logTailOptions.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </label>
          <label className="inline-check-field">
            <input
              checked={timestamps}
              type="checkbox"
              onChange={(event) => {
                setTimestamps(event.currentTarget.checked)
              }}
            />
            Timestamps
          </label>
          <label className="inline-check-field">
            <input
              checked={autoRefresh}
              type="checkbox"
              onChange={(event) => {
                setAutoRefresh(event.currentTarget.checked)
                setPaused(false)
              }}
            />
            Auto refresh
          </label>
          {autoRefresh && (
            <button
              className="secondary-button"
              type="button"
              onClick={() => {
                setPaused((value) => !value)
              }}
            >
              {paused ? 'Resume' : 'Pause'}
            </button>
          )}
          <button
            className="secondary-button"
            type="button"
            onClick={() => {
              setRefreshToken((value) => value + 1)
            }}
          >
            Refresh logs
          </button>
          {state.status === 'success' && (
            <button
              className="secondary-button"
              type="button"
              onClick={() => {
                downloadSnapshot(state.logs)
              }}
            >
              Download snapshot
            </button>
          )}
        </div>
      </div>

      {state.status === 'loading' && <p className="state-message">Loading logs...</p>}

      {state.status === 'error' && (
        <p className="state-message state-message-error">Unable to load logs.</p>
      )}

      {state.status === 'success' && visibleEntries.length === 0 && (
        <p className="empty-message">No logs found.</p>
      )}

      {state.status === 'success' && visibleEntries.length > 0 && (
        <div className="logs-output" role="log" aria-label="Container logs">
          {visibleEntries.map((entry, index) => (
            <div
              className={`log-line log-line-${entry.severity}`}
              key={`${entry.timestamp ?? 'no-time'}-${index}`}
            >
              {entry.timestamp && <span className="log-time">{entry.timestamp}</span>}
              <span className={`log-stream log-stream-${entry.stream}`}>{entry.stream}</span>
              <span className="log-message">{entry.message}</span>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}

function isLogTailOption(value: number): value is LogTailOption {
  return logTailOptions.some((option) => option === value)
}

function formatDate(value: string) {
  const date = new Date(value)

  if (Number.isNaN(date.getTime())) {
    return value
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

function resolveSeverity(line: string): ContainerLogEntry['severity'] {
  const lowered = line.toLowerCase()

  if (lowered.includes('error') || lowered.includes('fatal') || lowered.includes('exception')) {
    return 'error'
  }

  if (lowered.includes('warn')) {
    return 'warning'
  }

  if (lowered.includes('info')) {
    return 'info'
  }

  return 'default'
}

function downloadSnapshot(logs: ContainerLogsType) {
  const content = logs.entries.length > 0
    ? logs.entries
      .map((entry) => [
        entry.timestamp,
        entry.stream,
        entry.message,
      ].filter(Boolean).join(' '))
      .join('\n')
    : logs.lines.join('\n')
  const blob = new Blob([content], { type: 'text/plain;charset=utf-8' })
  const link = document.createElement('a')

  link.href = URL.createObjectURL(blob)
  link.download = `container-logs-${logs.containerId.slice(0, 12)}.txt`
  link.click()
  URL.revokeObjectURL(link.href)
}
