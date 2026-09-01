namespace GamesHud.Api.GameServers.Runtime;

public interface IGameRuntimeAdapter
{
    Task<RuntimeProviderOutcome> CreateAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken);
}

public sealed record RuntimeProviderOutcome(string Status, string? SafeCode = null, string? SafeMessage = null)
{
    public static RuntimeProviderOutcome Success(string message) => new(RuntimeMutationOutcomeStatuses.Success, SafeMessage: message);
    public static RuntimeProviderOutcome KnownFailure(string code, string message) => new(RuntimeMutationOutcomeStatuses.KnownFailure, code, message);
    public static RuntimeProviderOutcome Unknown(string code, string message) => new(RuntimeMutationOutcomeStatuses.Unknown, code, message);
}

public sealed class NoHostMutationGameRuntimeAdapter : IGameRuntimeAdapter
{
    public Task<RuntimeProviderOutcome> CreateAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RuntimeProviderOutcome.Success("Runtime mutation is not implemented in GH-10."));
    }
}
