using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;

namespace GamesHud.Api.Palworld.ManagedConfiguration;

public interface IPalworldManagedConfigurationTargetBuilder
{
    Task<PalworldManagedConfigurationTargetBuildResult> BuildAsync(
        ProvisioningContext context, CancellationToken cancellationToken);
    Task<PalworldManagedConfigurationTargetBuildResult> BuildForReconciliationAsync(
        string operationId, GameServerId gameServerId, CancellationToken cancellationToken);
}

public sealed class PalworldManagedConfigurationTargetBuilder : IPalworldManagedConfigurationTargetBuilder
{
    public const string StorageDefinitionId = "data";
    public const string RuntimeTarget = "/palworld";
    public static readonly string RelativeConfigurationPath =
        Path.Combine("Pal", "Saved", "Config", "LinuxServer", "PalWorldSettings.ini");

    private readonly IManagedStorageTargetBuilder _managedTargets;
    private readonly IGameDefinitionRegistry _definitions;

    public PalworldManagedConfigurationTargetBuilder(
        IManagedStorageTargetBuilder managedTargets,
        IGameDefinitionRegistry definitions)
    {
        _managedTargets = managedTargets;
        _definitions = definitions;
    }

    public async Task<PalworldManagedConfigurationTargetBuildResult> BuildAsync(
        ProvisioningContext context, CancellationToken cancellationToken)
    {
        if (!IsExpectedDefinition(context.GameDefinition)) return Failed();
        var built = await _managedTargets.BuildAsync(context, cancellationToken);
        return Build(context.OperationId, context.GameServerId, context.GameDefinition, built);
    }

    public async Task<PalworldManagedConfigurationTargetBuildResult> BuildForReconciliationAsync(
        string operationId, GameServerId gameServerId, CancellationToken cancellationToken)
    {
        if (!_definitions.TryGet(new GameId("palworld"), out var definition) || !IsExpectedDefinition(definition!))
            return Failed();
        var built = await _managedTargets.BuildForReconciliationAsync(operationId, gameServerId, cancellationToken);
        return Build(operationId, gameServerId, definition!, built);
    }

    private static PalworldManagedConfigurationTargetBuildResult Build(
        string operationId,
        GameServerId gameServerId,
        GameDefinition definition,
        ManagedStorageTargetBuildResult built)
    {
        if (!built.Succeeded || !IsSafeIdentifier(operationId) || !IsExpectedDefinition(definition)) return Failed();
        var target = built.Target!;
        if (target.GameServerId != gameServerId || target.OperationId != operationId) return Failed();
        var dataEntries = target.Entries.Where(entry => entry.StorageDefinitionId == StorageDefinitionId).ToArray();
        if (dataEntries.Length != 1) return Failed();

        try
        {
            var storageRoot = PalworldManagedPathSafety.EnsureContained(target.DataRoot, dataEntries[0].AbsolutePath);
            var destination = PalworldManagedPathSafety.EnsureContained(
                storageRoot, Path.Combine(storageRoot, RelativeConfigurationPath));
            if (destination.Equals(storageRoot, PalworldManagedPathSafety.PathComparison)) return Failed();
            var directory = Path.GetDirectoryName(destination);
            if (directory is null) return Failed();
            var prefix = $".PalWorldSettings.ini.gameshud-{operationId}-";
            return new(new(gameServerId, operationId, target.DataRoot, storageRoot, directory, destination, prefix), null, null);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or StoragePlanningException or PalworldManagedConfigurationException)
        {
            return Failed();
        }
    }

    private static bool IsExpectedDefinition(GameDefinition definition)
    {
        var data = definition.Storages.Where(item => item.Id == StorageDefinitionId).ToArray();
        return definition.GameId == new GameId("palworld")
            && data.Length == 1 && data[0].RuntimeTarget == RuntimeTarget;
    }

    private static bool IsSafeIdentifier(string value) => value.Length is > 0 and <= 80
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static PalworldManagedConfigurationTargetBuildResult Failed() => new(
        null,
        PalworldManagedConfigurationErrorCodes.TargetInvalid,
        "Managed Palworld configuration target could not be proven safe.");
}
