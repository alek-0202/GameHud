interface SectionHeaderProps {
  eyebrow?: string
  titleId?: string
  title: string
  description?: string
  aside?: string
}

export function SectionHeader({
  eyebrow,
  titleId,
  title,
  description,
  aside,
}: SectionHeaderProps) {
  return (
    <div className="section-header">
      <div>
        {eyebrow && <span className="section-eyebrow">{eyebrow}</span>}
        <h2 id={titleId}>{title}</h2>
        {description && <p>{description}</p>}
      </div>
      {aside && <span className="section-aside">{aside}</span>}
    </div>
  )
}
