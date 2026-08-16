import { useEffect, useRef, useState } from 'react'
import {
  PalworldApiRequestError,
  fetchPalworldConfig,
  updatePalworldConfig,
} from '../api/palworld'
import type { PalworldConfig, PalworldConfigUpdateRequest } from '../types/palworld'
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

interface PalworldFormState {
  serverName: string
  serverPassword: string
  expRate: string
  playerDamageRateAttack: string
  palCaptureRate: string
  playerStomachDecreaceRate: string
  playerStaminaDecreaceRate: string
  workSpeedRate: string
  collectionDropRate: string
  enemyDropItemRate: string
  palEggDefaultHatchingTime: string
  deathPenalty: string
  guildPlayerMaxNum: string
  baseCampMaxNum: string
  baseCampWorkerMaxNum: string
}

const deathPenaltyOptions = ['None', 'Item', 'ItemAndEquipment', 'All'] as const

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
  const [form, setForm] = useState<PalworldFormState>(
    initialConfig === null ? createEmptyForm() : createFormFromConfig(initialConfig),
  )
  const [feedback, setFeedback] = useState<Feedback | null>(null)
  const [pendingOperation, setPendingOperation] = useState<Operation | null>(null)
  const [isRestartConfirmationOpen, setIsRestartConfirmationOpen] = useState(false)
  const activeAbortControllerRef = useRef<AbortController | null>(null)
  const isMountedRef = useRef(true)

  const isProcessing = pendingOperation !== null

  useEffect(() => {
    void loadConfig()

    return () => {
      isMountedRef.current = false
      activeAbortControllerRef.current?.abort()
    }
  }, [])

  async function loadConfig() {
    const abortController = new AbortController()
    activeAbortControllerRef.current = abortController

    try {
      setState({ status: 'loading' })

      const config = await fetchPalworldConfig(abortController.signal)

      if (isMountedRef.current) {
        setState({ status: 'success', config })
        setForm(createFormFromConfig(config))
        onConfigLoaded?.(config)
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
    if (isProcessing) {
      return
    }

    const abortController = new AbortController()
    activeAbortControllerRef.current = abortController

    try {
      setPendingOperation(operation)
      setFeedback(null)

      const request = createUpdateRequest(form)
      const result = await updatePalworldConfig(
        request,
        operation === 'restart',
        abortController.signal,
      )
      const reloadedConfig = await fetchPalworldConfig(abortController.signal)

      if (isMountedRef.current) {
        setState({ status: 'success', config: reloadedConfig })
        setForm(createFormFromConfig(reloadedConfig))
        onConfigLoaded?.(reloadedConfig)
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

  return (
    <section className="page-section" aria-labelledby="palworld-settings-title">
      <SectionHeader
        eyebrow="Settings"
        titleId="palworld-settings-title"
        title="Palworld Settings"
        description="Supported quick config values. Empty password preserves the current server password."
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

      {state.status === 'success' && (
        <>
          <form
            className="palworld-form"
            onSubmit={(event) => {
              event.preventDefault()
              void submitForm('save')
            }}
          >
            <fieldset className="settings-group">
              <SectionHeader
                title="Gameplay"
                description="Combat, progression and work pacing."
              />
              <div className="settings-grid">
                <NumberField label="Experience rate" value={form.expRate} onChange={(value) => updateForm('expRate', value)} />
                <NumberField label="Player damage" value={form.playerDamageRateAttack} onChange={(value) => updateForm('playerDamageRateAttack', value)} />
                <NumberField label="Capture rate" value={form.palCaptureRate} onChange={(value) => updateForm('palCaptureRate', value)} />
                <NumberField label="Work speed" value={form.workSpeedRate} onChange={(value) => updateForm('workSpeedRate', value)} />
              </div>
            </fieldset>

            <fieldset className="settings-group">
              <SectionHeader
                title="World"
                description="Survival, drops, incubation and death behavior."
              />
              <div className="settings-grid">
                <NumberField label="Hunger rate" value={form.playerStomachDecreaceRate} onChange={(value) => updateForm('playerStomachDecreaceRate', value)} />
                <NumberField label="Stamina rate" value={form.playerStaminaDecreaceRate} onChange={(value) => updateForm('playerStaminaDecreaceRate', value)} />
                <NumberField label="Gather/drop rate" value={form.collectionDropRate} onChange={(value) => updateForm('collectionDropRate', value)} />
                <NumberField label="Enemy drop rate" value={form.enemyDropItemRate} onChange={(value) => updateForm('enemyDropItemRate', value)} />
                <NumberField label="Incubation" value={form.palEggDefaultHatchingTime} onChange={(value) => updateForm('palEggDefaultHatchingTime', value)} />
                <label className="form-field">
                  <span>Death penalty</span>
                  <select
                    disabled={isProcessing}
                    value={form.deathPenalty}
                    onChange={(event) => updateForm('deathPenalty', event.target.value)}
                  >
                    <option value="">Preserve current value</option>
                    {deathPenaltyOptions.map((option) => (
                      <option key={option} value={option}>
                        {option}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
            </fieldset>

            <fieldset className="settings-group">
              <SectionHeader
                title="Limits"
                description="Guild, base and worker limits."
              />
              <div className="settings-grid settings-grid-limits">
                <NumberField label="Guild player max" step="1" value={form.guildPlayerMaxNum} onChange={(value) => updateForm('guildPlayerMaxNum', value)} />
                <NumberField label="Base max" step="1" value={form.baseCampMaxNum} onChange={(value) => updateForm('baseCampMaxNum', value)} />
                <NumberField label="Worker max" step="1" value={form.baseCampWorkerMaxNum} onChange={(value) => updateForm('baseCampWorkerMaxNum', value)} />
              </div>
            </fieldset>

            <fieldset className="settings-group">
              <SectionHeader
                title="Server"
                description="Public name and optional password replacement."
              />
              <div className="settings-grid settings-grid-server">
                <TextField
                  label="Server name"
                  value={form.serverName}
                  onChange={(value) => updateForm('serverName', value)}
                />
                <TextField
                  label={state.config.hasServerPassword ? 'New server password' : 'Server password'}
                  type="password"
                  value={form.serverPassword}
                  onChange={(value) => updateForm('serverPassword', value)}
                />
              </div>
            </fieldset>

            <div className="palworld-actions">
              <button className="primary-button" disabled={isProcessing} type="submit">
                {pendingOperation === 'save' ? 'Saving...' : 'Save'}
              </button>
              <button
                className="danger-button"
                disabled={isProcessing}
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
              containerName={state.config.containerName}
              isProcessing={isProcessing}
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

  function updateForm(field: keyof PalworldFormState, value: string) {
    setForm((currentForm) => ({
      ...currentForm,
      [field]: value,
    }))
  }
}

interface FieldProps {
  label: string
  value: string
  onChange: (value: string) => void
}

function TextField({ label, value, onChange, type = 'text' }: FieldProps & { type?: 'text' | 'password' }) {
  return (
    <label className="form-field">
      <span>{label}</span>
      <input
        type={type}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  )
}

function NumberField({
  label,
  value,
  onChange,
  step = '0.1',
}: FieldProps & { step?: string }) {
  return (
    <label className="form-field">
      <span>{label}</span>
      <input
        min="0"
        step={step}
        type="number"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  )
}

interface ConfirmPalworldRestartDialogProps {
  containerName: string
  isProcessing: boolean
  onCancel: () => void
  onConfirm: () => void
}

function ConfirmPalworldRestartDialog({
  containerName,
  isProcessing,
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
        <h4 id="palworld-restart-title">Save and restart Palworld</h4>
        <p id="palworld-restart-description">
          This will stop and start {containerName}. Connected players will be disconnected and
          the server will have temporary downtime.
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
            disabled={isProcessing}
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

function createFormFromConfig(config: PalworldConfig): PalworldFormState {
  return {
    serverName: config.serverName ?? '',
    serverPassword: '',
    expRate: formatNumber(config.expRate),
    playerDamageRateAttack: formatNumber(config.playerDamageRateAttack),
    palCaptureRate: formatNumber(config.palCaptureRate),
    playerStomachDecreaceRate: formatNumber(config.playerStomachDecreaceRate),
    playerStaminaDecreaceRate: formatNumber(config.playerStaminaDecreaceRate),
    workSpeedRate: formatNumber(config.workSpeedRate),
    collectionDropRate: formatNumber(config.collectionDropRate),
    enemyDropItemRate: formatNumber(config.enemyDropItemRate),
    palEggDefaultHatchingTime: formatNumber(config.palEggDefaultHatchingTime),
    deathPenalty: config.deathPenalty ?? '',
    guildPlayerMaxNum: formatNumber(config.guildPlayerMaxNum),
    baseCampMaxNum: formatNumber(config.baseCampMaxNum),
    baseCampWorkerMaxNum: formatNumber(config.baseCampWorkerMaxNum),
  }
}

function createEmptyForm(): PalworldFormState {
  return {
    serverName: '',
    serverPassword: '',
    expRate: '',
    playerDamageRateAttack: '',
    palCaptureRate: '',
    playerStomachDecreaceRate: '',
    playerStaminaDecreaceRate: '',
    workSpeedRate: '',
    collectionDropRate: '',
    enemyDropItemRate: '',
    palEggDefaultHatchingTime: '',
    deathPenalty: '',
    guildPlayerMaxNum: '',
    baseCampMaxNum: '',
    baseCampWorkerMaxNum: '',
  }
}

function createUpdateRequest(form: PalworldFormState): PalworldConfigUpdateRequest {
  return {
    serverName: optionalText(form.serverName),
    serverPassword: optionalText(form.serverPassword),
    expRate: optionalNumber(form.expRate),
    playerDamageRateAttack: optionalNumber(form.playerDamageRateAttack),
    palCaptureRate: optionalNumber(form.palCaptureRate),
    playerStomachDecreaceRate: optionalNumber(form.playerStomachDecreaceRate),
    playerStaminaDecreaceRate: optionalNumber(form.playerStaminaDecreaceRate),
    workSpeedRate: optionalNumber(form.workSpeedRate),
    collectionDropRate: optionalNumber(form.collectionDropRate),
    enemyDropItemRate: optionalNumber(form.enemyDropItemRate),
    palEggDefaultHatchingTime: optionalNumber(form.palEggDefaultHatchingTime),
    deathPenalty: optionalText(form.deathPenalty),
    guildPlayerMaxNum: optionalNumber(form.guildPlayerMaxNum),
    baseCampMaxNum: optionalNumber(form.baseCampMaxNum),
    baseCampWorkerMaxNum: optionalNumber(form.baseCampWorkerMaxNum),
  }
}

function formatNumber(value: number | null) {
  return value === null ? '' : value.toString()
}

function optionalText(value: string) {
  const trimmedValue = value.trim()

  return trimmedValue.length === 0 ? null : trimmedValue
}

function optionalNumber(value: string) {
  const trimmedValue = value.trim()

  return trimmedValue.length === 0 ? null : Number(trimmedValue)
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
