import { SectionHeader } from '../components/SectionHeader'

export function SettingsPage() {
  return (
    <section className="page-section" aria-labelledby="settings-title">
      <SectionHeader
        eyebrow="System"
        titleId="settings-title"
        title="GamesHud Settings"
        description="General application settings will be added when authenticated administration exists."
      />

      <div className="summary-panel">
        <p>No general settings are available in this build.</p>
      </div>
    </section>
  )
}
