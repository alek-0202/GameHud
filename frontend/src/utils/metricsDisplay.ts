export function formatPercent(value: number | null) {
  if (value === null) {
    return '-'
  }

  return `${Math.round(value)}%`
}

export function formatBytes(value: number | null) {
  if (value === null) {
    return '-'
  }

  const gib = value / 1024 / 1024 / 1024

  if (gib >= 1) {
    return `${gib.toFixed(1)} GB`
  }

  const mib = value / 1024 / 1024

  return `${mib.toFixed(0)} MB`
}

export function formatBytePair(used: number | null, total: number | null) {
  return `${formatBytes(used)} / ${formatBytes(total)}`
}

export function toPercent(used: number | null, total: number | null) {
  if (used === null || total === null || total === 0) {
    return null
  }

  return Math.min(100, Math.max(0, used / total * 100))
}
