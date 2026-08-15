interface StatusBadgeProps {
  state: string
}

export function StatusBadge({ state }: StatusBadgeProps) {
  const normalizedState = state.trim().toLowerCase()
  const tone = getTone(normalizedState)

  return (
    <span className={`status-badge status-badge-${tone}`}>
      <span aria-hidden="true" />
      {state || 'Unknown'}
    </span>
  )
}

function getTone(state: string) {
  if (state === 'running') {
    return 'success'
  }

  if (['created', 'exited', 'stopped'].includes(state)) {
    return 'muted'
  }

  if (['restarting', 'paused'].includes(state)) {
    return 'warning'
  }

  if (state === 'dead') {
    return 'danger'
  }

  return 'neutral'
}
