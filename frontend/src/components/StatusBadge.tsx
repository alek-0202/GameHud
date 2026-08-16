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
  if (state === 'running' || state === 'online') {
    return 'success'
  }

  if (['created', 'exited', 'stopped', 'offline'].includes(state)) {
    return 'muted'
  }

  if (['restarting', 'paused', 'starting', 'rest unavailable'].includes(state)) {
    return 'warning'
  }

  if (state === 'dead' || state === 'not found') {
    return 'danger'
  }

  return 'neutral'
}
