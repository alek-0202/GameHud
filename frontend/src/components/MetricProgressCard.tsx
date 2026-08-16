interface MetricProgressCardProps {
  label: string
  value: string
  percent: number | null
}

export function MetricProgressCard({ label, value, percent }: MetricProgressCardProps) {
  const resolvedPercent = percent === null ? 0 : Math.min(100, Math.max(0, percent))

  return (
    <div className="metric-card metric-card-progress">
      <span>{label}</span>
      <strong>{value}</strong>
      <div
        aria-label={`${label} usage`}
        aria-valuemax={100}
        aria-valuemin={0}
        aria-valuenow={Math.round(resolvedPercent)}
        className="metric-progress"
        role="progressbar"
      >
        <span style={{ width: `${resolvedPercent}%` }} />
      </div>
    </div>
  )
}
