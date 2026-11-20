import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  fetchPalworldUpdateStatus,
  PalworldApiRequestError,
} from './palworld'

afterEach(() => {
  vi.restoreAllMocks()
})

describe('palworld api errors', () => {
  it('converts nginx html errors to readable messages', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(
      '<html><head><title>504 Gateway Time-out</title></head><body><center><h1>504 Gateway Time-out</h1></center></body></html>',
      {
        headers: {
          'content-type': 'text/html',
        },
        status: 504,
      },
    ))

    await expect(fetchPalworldUpdateStatus()).rejects.toMatchObject<Partial<PalworldApiRequestError>>({
      message: '504 Gateway Time-out',
      status: 504,
    })
  })
})
