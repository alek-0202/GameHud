using GamesHud.Api.Persistence.Configuration;
using GamesHud.Api.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Persistence;

public sealed class PersistenceInitializer : IPersistenceInitializer
{
    private static readonly SemaphoreSlim MigrationLock = new(1, 1);

    private readonly GamesHudDbContext _dbContext;
    private readonly IPersistenceLayoutResolver _layoutResolver;
    private readonly IOptions<PersistenceOptions> _options;

    public PersistenceInitializer(
        GamesHudDbContext dbContext,
        IPersistenceLayoutResolver layoutResolver,
        IOptions<PersistenceOptions> options)
    {
        _dbContext = dbContext;
        _layoutResolver = layoutResolver;
        _options = options;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var layout = _layoutResolver.ResolveLayout();
        Directory.CreateDirectory(layout.SystemRoot);

        if (!_options.Value.AutoMigrate)
        {
            return;
        }

        await MigrationLock.WaitAsync(cancellationToken);
        try
        {
            await _dbContext.Database.MigrateAsync(cancellationToken);
            await UpsertSchemaMetadataAsync(cancellationToken);
        }
        finally
        {
            MigrationLock.Release();
        }
    }

    private async Task UpsertSchemaMetadataAsync(CancellationToken cancellationToken)
    {
        var metadata = await _dbContext.PersistenceMetadata
            .SingleOrDefaultAsync(
                record => record.Id == PersistenceMetadataRecord.SchemaMetadataId,
                cancellationToken);

        if (metadata is null)
        {
            _dbContext.PersistenceMetadata.Add(new PersistenceMetadataRecord
            {
                Id = PersistenceMetadataRecord.SchemaMetadataId,
                Value = "persistence-foundation",
            });
        }
        else
        {
            metadata.Value = "persistence-foundation";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
