import { useParams } from 'react-router-dom'
import { PalworldSettings } from '../components/PalworldSettings'

export function PalworldSettingsPage() {
  const { serverId = 'palworld' } = useParams()

  return <PalworldSettings serverId={serverId} />
}
