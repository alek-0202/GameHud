import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useBeforeUnload } from 'react-router-dom'
import {
  PalworldApiRequestError,
  fetchPalworldConfig,
  updatePalworldConfig,
} from '../api/palworld'
import type {
  PalworldConfig,
  PalworldConfigUpdateRequest,
  PalworldSetting,
} from '../types/palworld'
import { getPalworldServerName } from '../utils/palworldSettings'
import { SectionHeader } from './SectionHeader'

type LoadState =
  | { status: 'loading' }
  | { status: 'success'; config: PalworldConfig }
  | { status: 'not-found' }
  | { status: 'unavailable' }
  | { status: 'error' }

type Operation = 'save' | 'restart'

type LoadErrorStatus = 'not-found' | 'unavailable' | 'error'

type Feedback = {
  type: 'success' | 'error'
  message: string
}

type EditorMode = 'basic' | 'advanced'

type FormValues = Record<string, string>

type ChangedSetting = PalworldSetting & {
  pendingValue: string
}

interface PalworldSettingsProps {
  initialConfig?: PalworldConfig | null
  onConfigLoaded?: (config: PalworldConfig) => void
}

export function PalworldSettings({
  initialConfig = null,
  onConfigLoaded,
}: PalworldSettingsProps) {
  const [state, setState] = useState<LoadState>(
    initialConfig === null
      ? { status: 'loading' }
      : { status: 'success', config: initialConfig },
  )
  const [values, setValues] = useState<FormValues>(
    initialConfig === null ? {} : createValuesFromConfig(initialConfig),
  )
  const [baselineValues, setBaselineValues] = useState<FormValues>(
    initialConfig === null ? {} : createValuesFromConfig(initialConfig),
  )
  const [expandedCategories, setExpandedCategories] = useState<Record<string, boolean>>({})
  const [searchTerm, setSearchTerm] = useState('')
  const [mode, setMode] = useState<EditorMode>('basic')
  const [feedback, setFeedback] = useState<Feedback | null>(null)
  const [pendingOperation, setPendingOperation] = useState<Operation | null>(null)
  const [isRestartConfirmationOpen, setIsRestartConfirmationOpen] = useState(false)
  const activeAbortControllerRef = useRef<AbortController | null>(null)
  const isMountedRef = useRef(true)

  const config = state.status === 'success' ? state.config : null
  const changedSettings = useMemo(
    () => (config === null ? [] : getChangedSettings(config.settings, values, baselineValues)),
    [baselineValues, config, values],
  )
  const changedCount = changedSettings.length
  const hasPendingChanges = changedCount > 0
  const isProcessing = pendingOperation !== null
  const groupedSettings = useMemo(
    () => (config === null ? [] : groupSettings(config.settings, values, baselineValues, mode, searchTerm)),
    [baselineValues, config, mode, searchTerm, values],
  )

  useBeforeUnload(
    useCallback(
      (event) => {
        if (!hasPendingChanges) {
          return
        }

        event.preventDefault()
        event.returnValue = ''
      },
      [hasPendingChanges],
    ),
  )

  useEffect(() => {
    void loadConfig()

    return () => {
      isMountedRef.current = false
      activeAbortControllerRef.current?.abort()
    }
  }, [])

  useEffect(() => {
    if (config === null) {
      return
    }

    setExpandedCategories((current) => {
      const next = { ...current }

      for (const category of getCategories(config.settings)) {
        next[category] ??= true
      }

      return next
    })
  }, [config])

  async function loadConfig() {
    const abortController = new AbortController()
    activeAbortControllerRef.current = abortController

    try {
      setState({ status: 'loading' })

      const loadedConfig = await fetchPalworldConfig(abortController.signal)

      if (isMountedRef.current) {
        acceptConfig(loadedConfig)
      }
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return
      }

      if (isMountedRef.current) {
        setState({ status: getLoadErrorStatus(error) })
      }
    }
  }

  async function submitForm(operation: Operation) {
    if (isProcessing || changedCount === 0) {
      return
    }

    const abortController = new AbortController()
    activeAbortControllerRef.current = abortController

    try {
      setPendingOperation(operation)
      setFeedback(null)

      const request = createUpdateRequest(changedSettings)
      const result = await updatePalworldConfig(
        request,
        operation === 'restart',
        abortController.signal,
      )

      if (isMountedRef.current) {
        acceptConfig(result.config)
        setFeedback({
          type: 'success',
          message: result.message,
        })
      }
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return
      }

      if (isMountedRef.current) {
        setFeedback({
          type: 'error',
          message: getSubmitErrorMessage(error),
        })
      }
    } finally {
      activeAbortControllerRef.current = null

      if (isMountedRef.current) {
        setPendingOperation(null)
        setIsRestartConfirmationOpen(false)
      }
    }
  }

  function acceptConfig(loadedConfig: PalworldConfig) {
    const nextValues = createValuesFromConfig(loadedConfig)

    setState({ status: 'success', config: loadedConfig })
    setValues(nextValues)
    setBaselineValues(nextValues)
    onConfigLoaded?.(loadedConfig)
  }

  function updateValue(key: string, value: string) {
    setValues((currentValues) => ({
      ...currentValues,
      [key]: value,
    }))
  }

  function resetSetting(key: string) {
    setValues((currentValues) => ({
      ...currentValues,
      [key]: baselineValues[key] ?? '',
    }))
  }

  function resetCategory(settingKeys: string[]) {
    setValues((currentValues) => {
      const nextValues = { ...currentValues }

      for (const settingKey of settingKeys) {
        nextValues[settingKey] = baselineValues[settingKey] ?? ''
      }

      return nextValues
    })
  }

  return (
    <section className="page-section" aria-labelledby="palworld-settings-title">
      <SectionHeader
        eyebrow="Settings"
        titleId="palworld-settings-title"
        title="Palworld Settings"
        description="World, server and lifecycle-sensitive settings from PalWorldSettings.ini."
      />

      {state.status === 'loading' && (
        <p className="state-message">Loading Palworld settings...</p>
      )}

      {state.status === 'not-found' && (
        <p className="state-message state-message-error">
          Palworld settings file was not found in the configured managed path.
        </p>
      )}

      {state.status === 'unavailable' && (
        <p className="state-message state-message-error">
          Palworld integration is not configured for this environment.
        </p>
      )}

      {state.status === 'error' && (
        <p className="state-message state-message-error">Unable to load Palworld settings.</p>
      )}

      {config !== null && (
        <>
          <form
            className="palworld-form"
            onSubmit={(event) => {
              event.preventDefault()
              void submitForm('save')
            }}
          >
            <div className="palworld-toolbar">
              <label className="form-field palworld-search">
                <span>Search settings</span>
                <input
                  disabled={isProcessing}
                  type="search"
                  value={searchTerm}
                  onChange={(event) => setSearchTerm(event.target.value)}
                />
              </label>

              <div className="segmented-control" aria-label="Palworld setting mode">
                <button
                  aria-pressed={mode === 'basic'}
                  className={mode === 'basic' ? 'segmented-control-active' : ''}
                  disabled={isProcessing}
                  type="button"
                  onClick={() => setMode('basic')}
                >
                  Basic
                </button>
                <button
                  aria-pressed={mode === 'advanced'}
                  className={mode === 'advanced' ? 'segmented-control-active' : ''}
                  disabled={isProcessing}
                  type="button"
                  onClick={() => setMode('advanced')}
                >
                  Advanced
                </button>
              </div>

              <div className="pending-count" aria-live="polite">
                <strong>{changedCount}</strong>
                <span>Pending</span>
              </div>
            </div>

            {groupedSettings.length === 0 && (
              <p className="empty-message">No settings match the current search.</p>
            )}

            {groupedSettings.map((group) => {
              const isExpanded = expandedCategories[group.category] ?? true
              const categoryChangedCount = group.settings.filter((setting) => setting.isChanged).length

              return (
                <fieldset className="settings-group" key={group.category}>
                  <div className="settings-category-header">
                    <button
                      aria-expanded={isExpanded}
                      className="settings-category-toggle"
                      type="button"
                      onClick={() => {
                        setExpandedCategories((current) => ({
                          ...current,
                          [group.category]: !isExpanded,
                        }))
                      }}
                    >
                      <span>{isExpanded ? '-' : '+'}</span>
                      <strong>{group.category}</strong>
                    </button>
                    <div className="settings-category-actions">
                      <span>{categoryChangedCount} changed</span>
                      <button
                        className="secondary-button compact-button"
                        disabled={isProcessing || categoryChangedCount === 0}
                        type="button"
                        onClick={() => resetCategory(group.settings.map((setting) => setting.key))}
                      >
                        Reset
                      </button>
                    </div>
                  </div>

                  {isExpanded && (
                    <div className="settings-grid settings-grid-editor">
                      {group.settings.map((setting) => (
                        <SettingField
                          disabled={isProcessing}
                          isChanged={setting.isChanged}
                          key={setting.key}
                          setting={setting}
                          value={values[setting.key] ?? ''}
                          onChange={(value) => updateValue(setting.key, value)}
                          onReset={() => resetSetting(setting.key)}
                        />
                      ))}
                    </div>
                  )}
                </fieldset>
              )
            })}

            <div className="palworld-actions">
              <button
                className="primary-button"
                disabled={isProcessing || changedCount === 0}
                type="submit"
              >
                {pendingOperation === 'save' ? 'Saving...' : 'Save'}
              </button>
              <button
                className="danger-button"
                disabled={isProcessing || changedCount === 0}
                type="button"
                onClick={() => setIsRestartConfirmationOpen(true)}
              >
                {pendingOperation === 'restart' ? 'Saving and restarting...' : 'Save & Restart'}
              </button>
            </div>
          </form>

          {isProcessing && (
            <p className="state-message">
              {pendingOperation === 'restart'
                ? 'Saving settings and restarting Palworld...'
                : 'Saving Palworld settings...'}
            </p>
          )}

          {feedback && (
            <p className={feedback.type === 'success' ? 'state-message state-message-success' : 'state-message state-message-error'}>
              {feedback.message}
            </p>
          )}

          {isRestartConfirmationOpen && (
            <ConfirmPalworldRestartDialog
              changedCount={changedCount}
              containerName={config.containerName}
              isProcessing={isProcessing}
              serverName={getPalworldServerName(config)}
              onCancel={() => setIsRestartConfirmationOpen(false)}
              onConfirm={() => {
                void submitForm('restart')
              }}
            />
          )}
        </>
      )}
    </section>
  )
}

interface SettingFieldProps {
  disabled: boolean
  isChanged: boolean
  setting: PalworldSetting
  value: string
  onChange: (value: string) => void
  onReset: () => void
}

function SettingField({
  disabled,
  isChanged,
  setting,
  value,
  onChange,
  onReset,
}: SettingFieldProps) {
  return (
    <div className={isChanged ? 'setting-card setting-card-changed' : 'setting-card'}>
      <div className="setting-card-header">
        <label className="setting-label" htmlFor={`palworld-setting-${setting.key}`}>
          {setting.label}
        </label>
        <button
          className="secondary-button compact-button"
          disabled={disabled || !isChanged}
          type="button"
          onClick={onReset}
        >
          Reset
        </button>
      </div>
      <p>{setting.description}</p>
      <SettingControl
        disabled={disabled}
        setting={setting}
        value={value}
        onChange={onChange}
      />
      <div className="setting-meta">
        <code>{setting.key}</code>
        {setting.defaultValue !== null && <span>Default {setting.defaultValue}</span>}
        {setting.securitySensitive && <span>Security</span>}
      </div>
    </div>
  )
}

function SettingControl({
  disabled,
  setting,
  value,
  onChange,
}: Omit<SettingFieldProps, 'isChanged' | 'onReset'>) {
  const inputId = `palworld-setting-${setting.key}`

  if (setting.type === 'boolean') {
    return (
      <label className="switch-field" htmlFor={inputId}>
        <input
          checked={value.toLowerCase() === 'true'}
          disabled={disabled}
          id={inputId}
          type="checkbox"
          onChange={(event) => onChange(event.target.checked ? 'True' : 'False')}
        />
        <span aria-hidden="true" />
      </label>
    )
  }

  if (setting.type === 'select') {
    return (
      <select
        disabled={disabled}
        id={inputId}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        {setting.options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    )
  }

  if (setting.type === 'integer' || setting.type === 'decimal') {
    return (
      <input
        disabled={disabled}
        id={inputId}
        max={setting.max ?? undefined}
        min={setting.min ?? undefined}
        step={setting.step ?? (setting.type === 'integer' ? 1 : 0.1)}
        type="number"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    )
  }

  return (
    <input
      disabled={disabled}
      id={inputId}
      placeholder={setting.type === 'password' && setting.hasValue ? 'Current value is protected' : undefined}
      type={setting.type === 'password' ? 'password' : 'text'}
      value={value}
      onChange={(event) => onChange(event.target.value)}
    />
  )
}

interface ConfirmPalworldRestartDialogProps {
  changedCount: number
  containerName: string
  isProcessing: boolean
  serverName: string
  onCancel: () => void
  onConfirm: () => void
}

function ConfirmPalworldRestartDialog({
  changedCount,
  containerName,
  isProcessing,
  serverName,
  onCancel,
  onConfirm,
}: ConfirmPalworldRestartDialogProps) {
  return (
    <div className="modal-backdrop" role="presentation">
      <div
        aria-describedby="palworld-restart-description"
        aria-labelledby="palworld-restart-title"
        aria-modal="true"
        className="modal-panel"
        role="dialog"
      >
        <h4 id="palworld-restart-title">Save and restart {serverName}</h4>
        <p id="palworld-restart-description">
          This applies {changedCount} setting changes to {containerName}. Connected players will
          be disconnected and the server will have temporary downtime.
        </p>
        <div className="modal-actions">
          <button
            className="secondary-button"
            disabled={isProcessing}
            type="button"
            onClick={onCancel}
          >
            Cancel
          </button>
          <button
            className="danger-button"
            disabled={isProcessing || changedCount === 0}
            type="button"
            onClick={onConfirm}
          >
            Save & Restart
          </button>
        </div>
      </div>
    </div>
  )
}

function createValuesFromConfig(config: PalworldConfig): FormValues {
  return Object.fromEntries(
    config.settings.map((setting) => [
      setting.key,
      setting.type === 'password' ? '' : setting.value ?? setting.defaultValue ?? '',
    ]),
  )
}

function getChangedSettings(
  settings: PalworldSetting[],
  values: FormValues,
  baselineValues: FormValues,
) {
  return settings.reduce<ChangedSetting[]>((changedSettings, setting) => {
    const value = normalizeFormValue(setting, values[setting.key] ?? '')
    const baselineValue = normalizeFormValue(setting, baselineValues[setting.key] ?? '')

    if (setting.type === 'password') {
      if (value.length > 0) {
        changedSettings.push({ ...setting, pendingValue: value })
      }

      return changedSettings
    }

    if (value !== baselineValue) {
      changedSettings.push({ ...setting, pendingValue: value })
    }

    return changedSettings
  }, [])
}

function createUpdateRequest(settings: ChangedSetting[]): PalworldConfigUpdateRequest {
  return {
    settings: settings.map((setting) => ({
      key: setting.key,
      value: setting.pendingValue,
    })),
  }
}

function normalizeFormValue(setting: PalworldSetting, value: string) {
  if (setting.type === 'string' || setting.type === 'password') {
    return value.trim()
  }

  return value
}

function getCategories(settings: PalworldSetting[]) {
  return [...new Set(settings.map((setting) => setting.category))]
}

function groupSettings(
  settings: PalworldSetting[],
  values: FormValues,
  baselineValues: FormValues,
  mode: EditorMode,
  searchTerm: string,
) {
  const normalizedSearch = searchTerm.trim().toLowerCase()
  const filteredSettings = settings
    .filter((setting) => mode === 'advanced' || !setting.advanced)
    .filter((setting) => {
      if (normalizedSearch.length === 0) {
        return true
      }

      return setting.label.toLowerCase().includes(normalizedSearch)
        || setting.description.toLowerCase().includes(normalizedSearch)
        || (mode === 'advanced' && setting.key.toLowerCase().includes(normalizedSearch))
    })

  return getCategories(filteredSettings).map((category) => ({
    category,
    settings: filteredSettings
      .filter((setting) => setting.category === category)
      .map((setting) => ({
        ...setting,
        isChanged: getChangedSettings([setting], values, baselineValues).length > 0,
      })),
  }))
}

function getLoadErrorStatus(error: unknown): LoadErrorStatus {
  if (error instanceof PalworldApiRequestError) {
    if (error.status === 404) {
      return 'not-found'
    }

    if (error.status === 503) {
      return 'unavailable'
    }
  }

  return 'error'
}

function getSubmitErrorMessage(error: unknown) {
  if (error instanceof PalworldApiRequestError) {
    if (error.status === 400) {
      return 'Some Palworld settings are invalid. Check the values and try again.'
    }

    if (error.status === 404) {
      return 'Palworld settings file or configured container was not found.'
    }

    if (error.status === 409) {
      return 'The configured Palworld container could not complete the lifecycle action.'
    }

    if (error.status === 503) {
      return 'Palworld or Docker is not configured for this environment.'
    }
  }

  return 'Unable to save Palworld settings.'
}
