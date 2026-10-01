using System.Security.Cryptography;
using System.Text;
using GamesHud.Api.Authentication;
using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.GameServers.Services;

public sealed record CreateManagedGameServerResult(
    bool Succeeded, bool Created, string? GameServerId, string? OperationId,
    string? ErrorCode, string? ErrorMessage);

public interface IManagedGameServerApplicationService
{
    Task<CreateManagedGameServerResult> CreateAsync(
        CreateManagedGameServerRequest? request, string? idempotencyKey, CancellationToken cancellationToken);
}

public sealed class ManagedGameServerApplicationService : IManagedGameServerApplicationService
{
    private const string FingerprintVersion = "gameshud.create-managed-game-server.v1";
    private readonly IGameServerProvisioningService _provisioning;
    private readonly ICurrentUser _currentUser;

    public ManagedGameServerApplicationService(IGameServerProvisioningService provisioning, ICurrentUser currentUser)
    { _provisioning = provisioning; _currentUser = currentUser; }

    public async Task<CreateManagedGameServerResult> CreateAsync(
        CreateManagedGameServerRequest? request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId))
            return Failure("authentication_required", "An authenticated owner is required.");
        if (!IsValidKey(idempotencyKey))
            return Failure("invalid_idempotency_key", "A valid Idempotency-Key header is required.");
        if (request is null || string.IsNullOrWhiteSpace(request.GameId)
            || string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 200
            || request.DisplayName.Any(char.IsControl))
            return Failure("invalid_request", "GameId and DisplayName are required and must be valid.");

        string gameId;
        try { gameId = new GameId(request.GameId).Value; }
        catch (ArgumentException) { return Failure("invalid_request", "GameId is invalid."); }
        var displayName = request.DisplayName.Trim();
        var fingerprint = Hash($"{FingerprintVersion}\n{gameId.Length}:{gameId}\n{displayName.Length}:{displayName}");
        var result = await _provisioning.ScheduleIdempotentProvisioningAsync(
            new CreateGameServerProvisioningRequest($"gs-{Guid.NewGuid():N}", gameId, displayName),
            _currentUser.UserId, Hash(idempotencyKey!), fingerprint, cancellationToken);
        return result.Succeeded
            ? new(true, result.Created, result.GameServerId, result.OperationId, null, null)
            : Failure(result.Failure!.Code, result.Failure.SafeMessage);
    }

    private static bool IsValidKey(string? key) => key is { Length: > 0 and <= 200 }
        && key.All(character => character is >= '!' and <= '~');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static CreateManagedGameServerResult Failure(string code, string message) => new(false, false, null, null, code, message);
}

public interface IManagedGameServerQueryService
{
    Task<ManagedGameServerResponse?> GetAsync(string ownerId, string id, CancellationToken cancellationToken);
    Task<ManagedProvisioningResponse?> GetProvisioningAsync(string ownerId, string id, CancellationToken cancellationToken);
}

public sealed class ManagedGameServerQueryService : IManagedGameServerQueryService
{
    private readonly GamesHudDbContext _db;
    public ManagedGameServerQueryService(GamesHudDbContext db) => _db = db;

    public async Task<ManagedGameServerResponse?> GetAsync(string ownerId, string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return await _db.ManagedGameServers.AsNoTracking().Where(server => server.OwnerId == ownerId && server.Id == id.Trim())
            .Select(server => new ManagedGameServerResponse(server.Id, server.GameId, server.DisplayName,
                server.InstallationType, server.RuntimeType, server.LifecycleState,
                server.CreatedAtUtc, server.UpdatedAtUtc)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ManagedProvisioningResponse?> GetProvisioningAsync(string ownerId, string id, CancellationToken cancellationToken)
    {
        var lifecycle = await _db.ManagedGameServers.AsNoTracking().Where(server => server.OwnerId == ownerId && server.Id == id)
            .Select(server => server.LifecycleState).SingleOrDefaultAsync(cancellationToken);
        if (lifecycle is null) return null;
        var operations = await _db.ProvisioningOperations.AsNoTracking().Include(item => item.Steps)
            .Where(item => item.GameServerId == id && item.Type == ProvisioningOperationTypes.Provision)
            .ToListAsync(cancellationToken);
        var operation = operations.OrderByDescending(item => item.StartedAtUtc).FirstOrDefault();
        if (operation is null) return null;
        var requiresInspection = lifecycle == ManagedGameServerLifecycleStates.ProvisioningBlocked
            || operation.Status == ProvisioningOperationStatuses.CompensationFailed
            || operation.Steps.Any(step => step.Status == ProvisioningStepStatuses.Failed
                && step.RetryClassification == ProvisioningRetryClassifications.RequiresInspection);
        var failed = operation.Status is ProvisioningOperationStatuses.Failed
            or ProvisioningOperationStatuses.CompensationFailed;
        ManagedProvisioningFailureResponse? failure = !failed && !requiresInspection ? null
            : new(requiresInspection ? "provisioning_requires_inspection" : SafeCode(operation.ErrorCode),
                requiresInspection ? "Provisioning requires operator inspection before it can continue."
                    : "Provisioning failed. Review the failure code and server state.", requiresInspection);
        return new ManagedProvisioningResponse(operation.Id, operation.GameServerId, operation.Status,
            operation.CurrentStep, operation.PipelineVersion, operation.StartedAtUtc, operation.UpdatedAtUtc,
            operation.CompletedAtUtc, requiresInspection, failure, operation.Steps.OrderBy(step => step.Sequence)
                .Select(step => new ManagedProvisioningStepResponse(step.StepId, step.Sequence, step.Status,
                    step.Attempt, step.MaxAttempts, step.StartedAtUtc, step.CompletedAtUtc)).ToArray());
    }

    private static string SafeCode(string? code) => code is not null && code.Length <= 120
        && code.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') ? code : "provisioning_failed";
}
