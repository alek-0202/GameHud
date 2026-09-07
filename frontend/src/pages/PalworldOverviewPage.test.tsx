import '@testing-library/jest-dom/vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  PalworldUpdateConfirmationDialog,
  PalworldUpdatePanel,
  updateConfirmation,
} from './PalworldOverviewPage'
import type { PalworldUpdateStatus } from '../types/palworld'

afterEach(() => {
  cleanup()
})

describe('PalworldUpdatePanel', () => {
  it('shows check loading state', () => {
    render(
      <PalworldUpdatePanel
        isChecking={true}
        isUpdating={false}
        message={null}
        onCheck={vi.fn()}
        onOpenUpdate={vi.fn()}
        update={null}
      />,
    )

    expect(screen.getByRole('button', { name: /checking/i })).toBeDisabled()
  })

  it('shows up to date state without update action', () => {
    renderPanel(createUpdateStatus({ updateStatus: 'up_to_date' }))

    expect(screen.getByText('Up to date')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /update server/i })).not.toBeInTheDocument()
  })

  it('shows update action only when update is available', () => {
    renderPanel(createUpdateStatus({ updateStatus: 'update_available' }))

    expect(screen.getByText('Update available')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /update server/i })).toBeEnabled()
  })

  it('does not offer update action when update-on-boot is not configured', () => {
    renderPanel(createUpdateStatus({
      message: 'A newer Palworld Steam manifest is available, but automatic updates are not configured for this server.',
      updateReady: false,
      updateReadinessMessage: 'UPDATE_ON_BOOT is not enabled on the configured Palworld container.',
      updateReadinessStatus: 'not_configured',
      updateStatus: 'update_available',
    }))

    expect(screen.getByText('Update available')).toBeInTheDocument()
    expect(screen.getByText('UPDATE_ON_BOOT is not enabled on the configured Palworld container.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /update server/i })).not.toBeInTheDocument()
  })

  it('shows unavailable check state with friendly status', () => {
    renderPanel(createUpdateStatus({
      availableBuild: null,
      availableVersion: null,
      message: 'Steam manifest information could not be checked.',
      updateStatus: 'unavailable',
    }))

    expect(screen.getAllByText('Check unavailable')).toHaveLength(2)
    expect(screen.queryByRole('button', { name: /update server/i })).not.toBeInTheDocument()
  })

  it('disables update action while update is running', () => {
    render(
      <PalworldUpdatePanel
        isChecking={false}
        isUpdating={true}
        message={null}
        onCheck={vi.fn()}
        onOpenUpdate={vi.fn()}
        update={createUpdateStatus({ updateStatus: 'update_available' })}
      />,
    )

    expect(screen.getByRole('button', { name: /update server/i })).toBeDisabled()
  })
})

describe('PalworldUpdateConfirmationDialog', () => {
  it('requires exact confirmation before submitting update', () => {
    const onSubmit = vi.fn()
    const onConfirmationTextChange = vi.fn()
    render(
      <PalworldUpdateConfirmationDialog
        confirmationText=""
        isUpdating={false}
        onCancel={vi.fn()}
        onConfirmationTextChange={onConfirmationTextChange}
        onSubmit={onSubmit}
        playersOnline={3}
        update={createUpdateStatus({ updateStatus: 'update_available' })}
      />,
    )

    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /update server/i })).toBeDisabled()

    fireEvent.change(screen.getByLabelText(/confirmation/i), {
      target: { value: updateConfirmation },
    })

    expect(onConfirmationTextChange).toHaveBeenCalledWith(updateConfirmation)
  })

  it('allows submit after exact confirmation and supports cancellation', () => {
    const onSubmit = vi.fn()
    const onCancel = vi.fn()
    render(
      <PalworldUpdateConfirmationDialog
        confirmationText={updateConfirmation}
        isUpdating={false}
        onCancel={onCancel}
        onConfirmationTextChange={vi.fn()}
        onSubmit={onSubmit}
        playersOnline={0}
        update={createUpdateStatus({ updateStatus: 'update_available' })}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /update server/i }))
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }))

    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(onCancel).toHaveBeenCalledTimes(1)
  })
})

function renderPanel(update: PalworldUpdateStatus) {
  render(
    <PalworldUpdatePanel
      isChecking={false}
      isUpdating={false}
      message={null}
      onCheck={vi.fn()}
      onOpenUpdate={vi.fn()}
      update={update}
    />,
  )
}

function createUpdateStatus(overrides: Partial<PalworldUpdateStatus>): PalworldUpdateStatus {
  return {
    availableBuild: '200',
    availableVersion: 'Steam manifest 200',
    installedBuild: '100',
    installedVersion: 'v1.0.3',
    lastCheckedAt: '2026-09-07T19:00:00.0000000Z',
    message: 'A newer Palworld Steam manifest appears to be available.',
    strategy: 'thijsvanloef-update-on-boot-steamcmd',
    updateReady: true,
    updateReadinessMessage: 'Automatic update-on-boot is configured on the current Palworld container.',
    updateReadinessStatus: 'ready',
    updateStatus: 'update_available',
    ...overrides,
  }
}
