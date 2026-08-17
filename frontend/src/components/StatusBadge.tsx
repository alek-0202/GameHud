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
  if ([
    'running',
    'online',
    'healthy',
    'success',
    'completed',
    'all systems operational',
  ].includes(state)) {
    return 'success'
  }

  if ([
    'created',
    'exited',
    'stopped',
    'offline',
    'dead',
    'not found',
    'failed',
    'error',
    'unhealthy',
    'needs attention',
  ].includes(state)) {
    return 'danger'
  }

  if ([
    'restarting',
    'paused',
    'starting',
    'pending',
    'rest unavailable',
    'update available',
    'attention',
    'warning',
    'partial telemetry',
  ].includes(state)) {
    return 'warning'
  }

  if (['backup', 'backups', 'scheduler', 'scheduled', 'automation'].includes(state)) {
    return 'automation'
  }

  if (['info', 'unknown', 'checking', 'checking systems'].includes(state)) {
    return 'info'
  }

  return 'neutral'
}
