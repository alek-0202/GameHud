namespace GamesHud.Api.GameServers.Runtime;

public interface IGameRuntimeAdapter
{
    Task<RuntimeAdapterResult> CreateAsync(ValidatedRuntimeMutationSpecification specification, CancellationToken cancellationToken);
}

public sealed record RuntimeAdapterResult(bool Executed, bool Succeeded, string SafeMessage);

public sealed class NoHostMutationGameRuntimeAdapter : IGameRuntimeAdapter
{
    public Task<RuntimeAdapterResult> CreateAsync(ValidatedRuntimeMutationSpecification specification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(specification);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RuntimeAdapterResult(false, true, "Runtime mutation is not implemented in SEC-03."));
    }
}
