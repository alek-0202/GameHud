import { PalworldSettings } from '../components/PalworldSettings'
import { usePalworldConfig } from '../hooks/usePalworldConfig'

export function PalworldSettingsPage() {
  const palworldState = usePalworldConfig()
  const config = palworldState.status === 'success' ? palworldState.config : null

  return (
    <PalworldSettings
      initialConfig={config}
    />
  )
}
