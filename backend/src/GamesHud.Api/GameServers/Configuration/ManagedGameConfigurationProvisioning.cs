using GamesHud.Api.GameServers.Artifacts;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.Palworld.ManagedConfiguration;

namespace GamesHud.Api.GameServers.Configuration;

public sealed class ConfigureManagedGameProvisioningStep(
    ConfigurePalworldGameProvisioningStep palworld,
    ConfigureMinecraftGameProvisioningStep minecraft) : IProvisioningStep
{
    public string Id => ProvisioningStepIds.ConfigureGame;

    public Task<ProvisioningStepResult> ExecuteAsync(
        ProvisioningContext context,
        CancellationToken cancellationToken) => context.ValidatedPlan.GameId.Value switch
    {
        "palworld" => palworld.ExecuteAsync(context, cancellationToken),
        "minecraft" => minecraft.ExecuteAsync(context, cancellationToken),
        _ => Task.FromResult(ProvisioningStepResult.Failure(
            GameConfigurationErrorCodes.Invalid,
            "Managed game configuration is not supported."))
    };
}

public sealed class ConfigureManagedGameReconciler(
    IProvisioningContextLoader contexts,
    ConfigurePalworldGameReconciler palworld,
    ConfigureMinecraftGameReconciler minecraft) : IProvisioningStepReconciler
{
    public string StepId => ProvisioningStepIds.ConfigureGame;

    public async Task<ProvisioningReconciliationResult> InspectAsync(
        ProvisioningOperationSnapshot operation,
        ProvisioningStepSnapshot step,
        CancellationToken cancellationToken)
    {
        var context = await contexts.LoadAsync(operation.OperationId, cancellationToken);
        return context.ValidatedPlan.GameId.Value switch
        {
            "palworld" => await palworld.InspectAsync(operation, step, cancellationToken),
            "minecraft" => await minecraft.InspectAsync(operation, step, cancellationToken),
            _ => new(ProvisioningReconciliationOutcomes.Ambiguous,
                "Managed game configuration state could not be proven safely.")
        };
    }
}

public sealed class ConfigureMinecraftGameProvisioningStep(
    IMinecraftProvisioningIntentReader intentReader) : IProvisioningStep
{
    public string Id => ProvisioningStepIds.ConfigureGame;

    public async Task<ProvisioningStepResult> ExecuteAsync(
        ProvisioningContext context,
        CancellationToken cancellationToken)
    {
        if (context.ValidatedPlan.GameId.Value != "minecraft")
            return ProvisioningStepResult.Failure(
                GameConfigurationErrorCodes.Invalid,
                "Managed Minecraft configuration ownership is invalid.");
        var target = context.ValidatedPlan.Storage.Single(item => item.DefinitionId == "data").ApiPath;
        if (string.IsNullOrWhiteSpace(target)) return InvalidTarget();
        var intent = await intentReader.ReadAsync(context.GameServerId, cancellationToken);
        try
        {
            var artifactMarker = Path.Combine(target, MinecraftArtifactAcquisitionService.MarkerFileName);
            var properties = Path.Combine(target, "server.properties");
            if (!File.Exists(artifactMarker) || !File.Exists(properties)
                || !File.ReadLines(properties).Any(line => line == "server-port=25565"))
                return InvalidTarget();
            var eulaPath = Path.Combine(target, "eula.txt");
            const string content = "# Accepted explicitly through GamesHud Managed provisioning.\neula=true\n";
            if (File.Exists(eulaPath))
            {
                if (File.ReadAllText(eulaPath).Replace("\r\n", "\n") != content)
                    return ProvisioningStepResult.Failure(
                        GameConfigurationErrorCodes.Conflict,
                        "Managed Minecraft EULA state conflicts with durable intent.");
                return ProvisioningStepResult.Success();
            }
            var temporary = Path.Combine(target, $".eula-{context.OperationId}.tmp");
            await File.WriteAllTextAsync(temporary, content, cancellationToken);
            File.Move(temporary, eulaPath);
            return ProvisioningStepResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ProvisioningStepResult.Failure(
                GameConfigurationErrorCodes.PersistenceFailed,
                "Managed Minecraft configuration could not be written.",
                ProvisioningFailureTypes.Unknown);
        }
    }

    private static ProvisioningStepResult InvalidTarget() => ProvisioningStepResult.Failure(
        GameConfigurationErrorCodes.Invalid,
        "Managed Minecraft artifact state is invalid.");
}

public sealed class ConfigureMinecraftGameReconciler(
    IProvisioningContextLoader contexts,
    IMinecraftProvisioningIntentReader intentReader) : IProvisioningStepReconciler
{
    public string StepId => ProvisioningStepIds.ConfigureGame;

    public async Task<ProvisioningReconciliationResult> InspectAsync(
        ProvisioningOperationSnapshot operation,
        ProvisioningStepSnapshot step,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await contexts.LoadAsync(operation.OperationId, cancellationToken);
            _ = await intentReader.ReadAsync(context.GameServerId, cancellationToken);
            var target = context.ValidatedPlan.Storage.Single(item => item.DefinitionId == "data").ApiPath;
            if (string.IsNullOrWhiteSpace(target)) return Ambiguous();
            var eulaPath = Path.Combine(target, "eula.txt");
            if (!File.Exists(eulaPath))
                return new(ProvisioningReconciliationOutcomes.EffectAbsent,
                    "Managed Minecraft EULA configuration is absent.");
            return File.ReadAllText(eulaPath).Replace("\r\n", "\n")
                == "# Accepted explicitly through GamesHud Managed provisioning.\neula=true\n"
                ? new(ProvisioningReconciliationOutcomes.EffectExists,
                    "Managed Minecraft EULA configuration matches durable intent.")
                : Ambiguous();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Ambiguous(); }
    }

    private static ProvisioningReconciliationResult Ambiguous() => new(
        ProvisioningReconciliationOutcomes.Ambiguous,
        "Managed Minecraft configuration state could not be proven safely.");
}
