using GamesHud.Api.GameServers.Configuration;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Secrets.Models;
using GamesHud.Api.Secrets.Services;

namespace GamesHud.Api.Palworld.ManagedConfiguration;

public interface IPalworldManagedConfigurationIntentReader
{
    Task<PalworldManagedConfiguration> ReadAsync(GameServerId gameServerId, CancellationToken cancellationToken);
}

public sealed class PalworldManagedConfigurationIntentReader : IPalworldManagedConfigurationIntentReader
{
    private readonly IGameProvisioningConfigurationStore _configurations;
    private readonly PalworldProvisioningConfigurationCodec _codec;
    private readonly ISecretStore _secrets;
    private readonly IPalworldManagedConfigurationSerializer _serializer;

    public PalworldManagedConfigurationIntentReader(
        IGameProvisioningConfigurationStore configurations,
        PalworldProvisioningConfigurationCodec codec,
        ISecretStore secrets,
        IPalworldManagedConfigurationSerializer serializer)
    {
        _configurations = configurations;
        _codec = codec;
        _secrets = secrets;
        _serializer = serializer;
    }

    public async Task<PalworldManagedConfiguration> ReadAsync(
        GameServerId gameServerId, CancellationToken cancellationToken)
    {
        var durable = await _configurations.LoadAsync(gameServerId.ToString(), _codec, cancellationToken);
        if (durable.GameId != new GameId("palworld")
            || durable.ConfigurationKind != PalworldProvisioningConfigurationCodec.InitialConfigurationKind
            || durable.SchemaVersion != PalworldProvisioningConfigurationCodec.CurrentSchemaVersion
            || durable.TypedValue is not PalworldInitialProvisioningConfiguration intent)
            throw new PalworldManagedConfigurationException(
                PalworldManagedConfigurationErrorCodes.IntentInvalid,
                "Managed Palworld configuration intent is invalid.");

        var serverPassword = await ResolveAsync(intent.ServerPasswordSecretReference, cancellationToken);
        var adminPassword = await ResolveAsync(intent.AdminPasswordSecretReference, cancellationToken);
        var resolved = new PalworldManagedConfiguration(intent.ServerName, intent.ServerDescription,
            intent.MaxPlayers, intent.Difficulty, serverPassword, adminPassword);
        _ = _serializer.Serialize(resolved);
        return resolved;
    }

    private async Task<string> ResolveAsync(SecretReference? reference, CancellationToken cancellationToken) =>
        reference is null ? string.Empty : (await _secrets.GetAsync(reference, cancellationToken)).Reveal();
}

public sealed class ConfigurePalworldGameProvisioningStep : IProvisioningStep
{
    private readonly IPalworldManagedConfigurationTargetBuilder _targets;
    private readonly IPalworldManagedConfigurationIntentReader _intent;
    private readonly IPalworldManagedConfigurationFileStore _files;
    private readonly ILogger<ConfigurePalworldGameProvisioningStep> _logger;

    public ConfigurePalworldGameProvisioningStep(
        IPalworldManagedConfigurationTargetBuilder targets,
        IPalworldManagedConfigurationIntentReader intent,
        IPalworldManagedConfigurationFileStore files,
        ILogger<ConfigurePalworldGameProvisioningStep> logger)
    {
        _targets = targets;
        _intent = intent;
        _files = files;
        _logger = logger;
    }

    public string Id => ProvisioningStepIds.ConfigureGame;

    public async Task<ProvisioningStepResult> ExecuteAsync(
        ProvisioningContext context, CancellationToken cancellationToken)
    {
        try
        {
            var built = await _targets.BuildAsync(context, cancellationToken);
            if (!built.Succeeded)
                return ProvisioningStepResult.Failure(built.SafeErrorCode!, built.SafeMessage!);

            var expected = await _intent.ReadAsync(context.GameServerId, cancellationToken);
            var result = await _files.MaterializeAsync(built.Target!, expected, cancellationToken);
            _logger.LogInformation(
                "Managed Palworld configuration for operation {OperationId} and server {GameServerId} completed with {Outcome} and safe code {SafeErrorCode}",
                context.OperationId, context.GameServerId, result.Status, result.SafeErrorCode);
            return result.Status switch
            {
                PalworldManagedConfigurationMutationStatuses.Success => ProvisioningStepResult.Success(),
                PalworldManagedConfigurationMutationStatuses.KnownFailure => ProvisioningStepResult.Failure(
                    result.SafeErrorCode ?? PalworldManagedConfigurationErrorCodes.WriteFailed, result.SafeMessage),
                _ => ProvisioningStepResult.Failure(
                    result.SafeErrorCode ?? PalworldManagedConfigurationErrorCodes.OutcomeUnknown,
                    result.SafeMessage, ProvisioningFailureTypes.Unknown)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (GameConfigurationException exception)
        {
            return ProvisioningStepResult.Failure(exception.Code, exception.Message);
        }
        catch (SecretStoreException)
        {
            return ProvisioningStepResult.Failure(
                PalworldManagedConfigurationErrorCodes.SecretUnavailable,
                "A required managed Palworld configuration secret is unavailable.");
        }
        catch (PalworldManagedConfigurationException exception)
        {
            return ProvisioningStepResult.Failure(exception.Code, exception.Message);
        }
    }
}

public sealed class ConfigurePalworldGameReconciler : IProvisioningStepReconciler
{
    private readonly IPalworldManagedConfigurationTargetBuilder _targets;
    private readonly IPalworldManagedConfigurationIntentReader _intent;
    private readonly IPalworldManagedConfigurationFileStore _files;

    public ConfigurePalworldGameReconciler(
        IPalworldManagedConfigurationTargetBuilder targets,
        IPalworldManagedConfigurationIntentReader intent,
        IPalworldManagedConfigurationFileStore files)
    {
        _targets = targets;
        _intent = intent;
        _files = files;
    }

    public string StepId => ProvisioningStepIds.ConfigureGame;

    public async Task<ProvisioningReconciliationResult> InspectAsync(
        ProvisioningOperationSnapshot operation,
        ProvisioningStepSnapshot step,
        CancellationToken cancellationToken)
    {
        try
        {
            var serverId = new GameServerId(operation.GameServerId);
            var built = await _targets.BuildForReconciliationAsync(
                operation.OperationId, serverId, cancellationToken);
            if (!built.Succeeded) return Ambiguous();
            var expected = await _intent.ReadAsync(serverId, cancellationToken);
            return await _files.InspectAsync(built.Target!, expected, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return Ambiguous();
        }
    }

    private static ProvisioningReconciliationResult Ambiguous() => new(
        ProvisioningReconciliationOutcomes.Ambiguous,
        "Managed Palworld configuration state could not be proven safely.");
}
