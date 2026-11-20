namespace GamesHud.Api.Palworld.Updates.Services;

public sealed class PalworldUpdateOperationState
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public IDisposable? TryBegin()
    {
        return _semaphore.Wait(0)
            ? new Releaser(_semaphore)
            : null;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _semaphore.Release();
            _disposed = true;
        }
    }
}
