import type { PalworldConfig, PalworldSetting } from '../types/palworld'

export function findPalworldSetting(
  config: PalworldConfig | null,
  key: string,
): PalworldSetting | null {
  return config?.settings.find((setting) => setting.key === key) ?? null
}

export function getPalworldSettingValue(config: PalworldConfig | null, key: string) {
  return findPalworldSetting(config, key)?.value ?? null
}

export function getPalworldServerName(config: PalworldConfig | null) {
  return getPalworldSettingValue(config, 'ServerName') || 'Palworld'
}

export function hasPalworldServerPassword(config: PalworldConfig | null) {
  return findPalworldSetting(config, 'ServerPassword')?.hasValue ?? false
}
