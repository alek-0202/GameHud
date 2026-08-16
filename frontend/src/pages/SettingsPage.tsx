import { useEffect, useMemo, useState } from 'react'
import {
  fetchNotificationSettings,
  fetchScheduleTasks,
  runScheduleTask,
  saveScheduleTask,
  sendTestNotification,
} from '../api/operations'
import { SectionHeader } from '../components/SectionHeader'
import type {
  NotificationSettings,
  ScheduleActionType,
  ScheduleTask,
} from '../types/operations'

type SettingsState =
  | { status: 'loading' }
  | { status: 'success'; notifications: NotificationSettings; tasks: ScheduleTask[] }
  | { status: 'error'; message: string }

const actionLabels: Record<ScheduleActionType, string> = {
  'automatic-backup': 'Automatic backup',
  'restart-palworld': 'Palworld restart',
  'update-check': 'Update check',
  announcement: 'Announcement',
  'shutdown-palworld': 'Palworld shutdown',
}

export function SettingsPage() {
  const [state, setState] = useState<SettingsState>({ status: 'loading' })
  const [testStatus, setTestStatus] = useState<string | null>(null)
  const [savingTaskId, setSavingTaskId] = useState<string | null>(null)

  async function loadSettings(signal?: AbortSignal) {
    try {
      setState({ status: 'loading' })

      const [notifications, tasks] = await Promise.all([
        fetchNotificationSettings(signal),
        fetchScheduleTasks(signal),
      ])

      setState({ status: 'success', notifications, tasks })
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return
      }

      setState({ status: 'error', message: 'Unable to load operational settings.' })
    }
  }

  useEffect(() => {
    const abortController = new AbortController()

    void loadSettings(abortController.signal)

    return () => {
      abortController.abort()
    }
  }, [])

  const sortedTasks = useMemo(() => {
    if (state.status !== 'success') {
      return []
    }

    return [...state.tasks].sort((first, second) => first.id.localeCompare(second.id))
  }, [state])

  return (
    <section className="page-section" aria-labelledby="settings-title">
      <SectionHeader
        eyebrow="System"
        titleId="settings-title"
        title="GamesHud Settings"
        description="Operational settings for notifications and safe scheduled tasks."
      />

      {state.status === 'loading' && <p className="state-message">Loading settings...</p>}

      {state.status === 'error' && (
        <p className="state-message state-message-error">{state.message}</p>
      )}

      {state.status === 'success' && (
        <div className="settings-panel-grid">
          <section className="summary-panel" aria-labelledby="notifications-title">
            <SectionHeader titleId="notifications-title" title="Notifications" />
            <dl className="settings-status-list">
              <div>
                <dt>Discord webhook configured</dt>
                <dd>{state.notifications.discordWebhookConfigured ? 'Yes' : 'No'}</dd>
              </div>
              <div>
                <dt>Cooldown</dt>
                <dd>{state.notifications.cooldownSeconds}s</dd>
              </div>
              <div>
                <dt>Last test</dt>
                <dd>{formatOptionalDate(state.notifications.lastTestAt)}</dd>
              </div>
            </dl>

            <div className="notification-grid">
              <ReadonlyToggle label="Server status" checked={state.notifications.serverStatusEnabled} />
              <ReadonlyToggle label="Backups" checked={state.notifications.backupsEnabled} />
              <ReadonlyToggle label="Updates" checked={state.notifications.updatesEnabled} />
              <ReadonlyToggle label="Player join/leave" checked={state.notifications.playerJoinLeaveEnabled} />
            </div>

            <div className="palworld-actions">
              <button
                className="secondary-button"
                type="button"
                onClick={async () => {
                  setTestStatus('Sending test notification...')
                  try {
                    const result = await sendTestNotification()
                    setTestStatus(result.message)
                    await loadSettings()
                  } catch {
                    setTestStatus('Unable to send test notification.')
                  }
                }}
              >
                Test notification
              </button>
              {testStatus && <span className="section-aside">{testStatus}</span>}
            </div>
          </section>

          <section className="summary-panel" aria-labelledby="scheduler-title">
            <SectionHeader titleId="scheduler-title" title="Scheduler" />

            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>Task</th>
                    <th>Next Run</th>
                    <th>Status</th>
                    <th>Last Result</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {sortedTasks.map((task) => (
                    <SchedulerRow
                      key={task.id}
                      disabled={savingTaskId === task.id}
                      task={task}
                      onSave={async (nextTask) => {
                        setSavingTaskId(task.id)
                        try {
                          await saveScheduleTask(nextTask)
                          await loadSettings()
                        } finally {
                          setSavingTaskId(null)
                        }
                      }}
                      onRun={async () => {
                        setSavingTaskId(task.id)
                        try {
                          await runScheduleTask(task.id)
                          await loadSettings()
                        } finally {
                          setSavingTaskId(null)
                        }
                      }}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      )}
    </section>
  )
}

interface ReadonlyToggleProps {
  label: string
  checked: boolean
}

function ReadonlyToggle({ label, checked }: ReadonlyToggleProps) {
  return (
    <label className="inline-check-field">
      <input checked={checked} readOnly type="checkbox" />
      {label}
    </label>
  )
}

interface SchedulerRowProps {
  task: ScheduleTask
  disabled: boolean
  onSave: (task: ScheduleTask) => Promise<void>
  onRun: () => Promise<void>
}

function SchedulerRow({
  task,
  disabled,
  onSave,
  onRun,
}: SchedulerRowProps) {
  const [enabled, setEnabled] = useState(task.enabled)
  const [recurrenceMinutes, setRecurrenceMinutes] = useState(task.recurrenceMinutes)
  const [message, setMessage] = useState(task.message ?? '')

  useEffect(() => {
    setEnabled(task.enabled)
    setRecurrenceMinutes(task.recurrenceMinutes)
    setMessage(task.message ?? '')
  }, [task])

  return (
    <tr>
      <td>
        <strong>{actionLabels[task.actionType]}</strong>
        <span className="table-subtext">{task.id}</span>
        {(task.actionType === 'announcement' || task.actionType === 'restart-palworld') && (
          <input
            aria-label={`${task.id} message`}
            disabled={disabled}
            type="text"
            value={message}
            onChange={(event) => {
              setMessage(event.currentTarget.value)
            }}
          />
        )}
      </td>
      <td>{formatOptionalDate(task.nextRunAt)}</td>
      <td>{enabled ? task.status : 'disabled'}</td>
      <td>{task.lastResult}</td>
      <td>
        <div className="scheduler-actions">
          <label className="inline-check-field">
            <input
              checked={enabled}
              disabled={disabled}
              type="checkbox"
              onChange={(event) => {
                setEnabled(event.currentTarget.checked)
              }}
            />
            Enabled
          </label>
          <input
            aria-label={`${task.id} recurrence minutes`}
            disabled={disabled}
            min={1}
            max={10080}
            type="number"
            value={recurrenceMinutes}
            onChange={(event) => {
              setRecurrenceMinutes(Number(event.currentTarget.value))
            }}
          />
          <button
            className="secondary-button compact-button"
            disabled={disabled}
            type="button"
            onClick={() => {
              void onSave({
                ...task,
                enabled,
                recurrenceMinutes,
                message: message.trim() || null,
              })
            }}
          >
            Save
          </button>
          <button
            className="secondary-button compact-button"
            disabled={disabled || !enabled}
            type="button"
            onClick={() => {
              void onRun()
            }}
          >
            Run
          </button>
        </div>
      </td>
    </tr>
  )
}

function formatOptionalDate(value: string | null) {
  if (!value) {
    return 'Not scheduled'
  }

  const date = new Date(value)

  if (Number.isNaN(date.getTime())) {
    return value
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}
