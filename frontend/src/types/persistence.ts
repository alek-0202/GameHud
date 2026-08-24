export interface PersistenceStatus {
  available: boolean
  provider: string
  migrationStatus: string
  appliedMigration: string | null
  errorCode: string | null
}
