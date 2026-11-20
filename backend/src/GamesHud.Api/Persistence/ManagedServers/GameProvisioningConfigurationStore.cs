using GamesHud.Api.GameServers.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.Persistence.ManagedServers;

public interface IGameProvisioningConfigurationStore
{
    Task<ValidatedGameProvisioningConfiguration> EnsureAsync(string gameServerId,
        ValidatedGameProvisioningConfiguration configuration, IGameProvisioningConfigurationCodec codec,
        CancellationToken cancellationToken = default);
    Task<ValidatedGameProvisioningConfiguration> LoadAsync(string gameServerId,
        IGameProvisioningConfigurationCodec codec, CancellationToken cancellationToken = default);
}

public sealed class GameProvisioningConfigurationStore(GamesHudDbContext dbContext)
    : IGameProvisioningConfigurationStore
{
    public async Task<ValidatedGameProvisioningConfiguration> EnsureAsync(string gameServerId,
        ValidatedGameProvisioningConfiguration configuration, IGameProvisioningConfigurationCodec codec,
        CancellationToken cancellationToken = default)
    {
        var server = await dbContext.ManagedGameServers.SingleOrDefaultAsync(item => item.Id == gameServerId, cancellationToken);
        if (server is null || server.InstallationType != Models.ManagedInstallationTypes.Managed
            || server.GameId != configuration.GameId.Value || codec.GameId != configuration.GameId)
            throw new GameConfigurationException(GameConfigurationErrorCodes.Invalid, "Managed game configuration ownership is invalid.");
        var existing = await dbContext.ManagedGameConfigurations.SingleOrDefaultAsync(item =>
            item.GameServerId == gameServerId && item.ConfigurationKind == configuration.ConfigurationKind, cancellationToken);
        if (existing is not null)
        {
            if (existing.GameId == configuration.GameId.Value && existing.SchemaVersion == configuration.SchemaVersion
                && existing.Payload == configuration.Payload)
                return codec.Deserialize(existing.GameId, existing.ConfigurationKind, existing.SchemaVersion, existing.Payload);
            throw new GameConfigurationException(GameConfigurationErrorCodes.Conflict, "Managed game configuration conflicts with durable intent.");
        }
        dbContext.ManagedGameConfigurations.Add(new Models.ManagedGameConfigurationRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            GameServerId = gameServerId,
            GameId = configuration.GameId.Value,
            ConfigurationKind = configuration.ConfigurationKind,
            SchemaVersion = configuration.SchemaVersion,
            Payload = configuration.Payload,
            Version = 1
        });
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            throw new GameConfigurationException(GameConfigurationErrorCodes.Conflict, "Managed game configuration changed concurrently.");
        }
        return configuration;
    }

    public async Task<ValidatedGameProvisioningConfiguration> LoadAsync(string gameServerId,
        IGameProvisioningConfigurationCodec codec, CancellationToken cancellationToken = default)
    {
        var server = await dbContext.ManagedGameServers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == gameServerId, cancellationToken);
        if (server is null || server.InstallationType != Models.ManagedInstallationTypes.Managed)
            throw new GameConfigurationException(GameConfigurationErrorCodes.Missing, "Managed game configuration was not found.");
        var record = await dbContext.ManagedGameConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameServerId == gameServerId
                && item.ConfigurationKind == codec.ConfigurationKind, cancellationToken);
        if (record is null)
            throw new GameConfigurationException(GameConfigurationErrorCodes.Missing, "Managed game configuration was not found.");
        if (record.GameId != server.GameId || record.GameId != codec.GameId.Value)
            throw new GameConfigurationException(GameConfigurationErrorCodes.Invalid, "Managed game configuration ownership is invalid.");
        return codec.Deserialize(record.GameId, record.ConfigurationKind, record.SchemaVersion, record.Payload);
    }
}
