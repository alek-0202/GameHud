interface MiniMetricChartProps {
  label: string
  values: Array<number | null>
  unit?: string
  max?: number
}

export function MiniMetricChart({
  label,
  values,
  unit = '',
  max,
}: MiniMetricChartProps) {
  const numericValues = values.filter((value): value is number => value !== null)

  if (numericValues.length < 2) {
    return (
      <div className="metric-chart-card">
        <span className="section-eyebrow">{label}</span>
        <p className="empty-chart-message">Not enough history yet.</p>
      </div>
    )
  }

  const upperBound = max ?? Math.max(...numericValues, 1)
  const points = values
    .map((value, index) => {
      const x = values.length === 1 ? 0 : index / (values.length - 1) * 100
      const safeValue = value ?? 0
      const y = 34 - Math.min(34, Math.max(0, safeValue / upperBound * 34))

      return `${x.toFixed(2)},${y.toFixed(2)}`
    })
    .join(' ')
  const latestValue = numericValues.at(-1) ?? 0

  return (
    <div className="metric-chart-card">
      <div className="metric-chart-heading">
        <span className="section-eyebrow">{label}</span>
        <strong>{formatLatest(latestValue, unit)}</strong>
      </div>
      <svg
        aria-label={`${label} history`}
        className="metric-chart"
        preserveAspectRatio="none"
        viewBox="0 0 100 36"
      >
        <polyline points={points} />
      </svg>
    </div>
  )
}

function formatLatest(value: number, unit: string) {
  if (unit === '%') {
    return `${Math.round(value)}%`
  }

  return `${Math.round(value)}${unit}`
}
