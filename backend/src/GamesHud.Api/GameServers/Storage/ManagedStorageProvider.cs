using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Runtime;

namespace GamesHud.Api.GameServers.Storage;

public interface IManagedStorageProvider
{
    Task<RuntimeProviderOutcome> PrepareAsync(ValidatedManagedStorageTarget target, CancellationToken cancellationToken);
    Task<ProvisioningReconciliationResult> InspectAsync(ValidatedManagedStorageTarget target, CancellationToken cancellationToken);
}

public interface IManagedDirectoryOperations
{
    bool Exists(string path);
    FileAttributes GetAttributes(string path);
    void Create(string path);
}

public sealed class SystemManagedDirectoryOperations : IManagedDirectoryOperations
{
    public bool Exists(string path) => Directory.Exists(path);
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public void Create(string path) => Directory.CreateDirectory(path);
}

public sealed class ManagedStorageProvider : IManagedStorageProvider
{
    private readonly IManagedDirectoryOperations _directories;

    public ManagedStorageProvider(IManagedDirectoryOperations directories) => _directories = directories;

    public Task<RuntimeProviderOutcome> PrepareAsync(ValidatedManagedStorageTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<RuntimeProviderOutcome>(cancellationToken);

        foreach (var entry in target.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safety = InspectPathSafety(target.DataRoot, entry.AbsolutePath);
            if (!safety.Safe)
                return Task.FromResult(RuntimeProviderOutcome.KnownFailure(ManagedStorageErrorCodes.TargetInvalid, "Managed storage target is unsafe."));

            try
            {
                _directories.Create(entry.AbsolutePath);
                if (!InspectPathSafety(target.DataRoot, entry.AbsolutePath).Safe || !_directories.Exists(entry.AbsolutePath))
                    return Task.FromResult(RuntimeProviderOutcome.Unknown(
                        ManagedStorageErrorCodes.ReconciliationAmbiguous, "Managed storage preparation outcome is unknown."));
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(RuntimeProviderOutcome.KnownFailure(
                    ManagedStorageErrorCodes.AccessDenied, "Managed storage could not be prepared because access was denied."));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Task.FromResult(RuntimeProviderOutcome.KnownFailure(
                    ManagedStorageErrorCodes.TargetInvalid, "Managed storage target is invalid."));
            }
            catch (IOException)
            {
                var inspection = InspectEntry(target.DataRoot, entry.AbsolutePath);
                return Task.FromResult(inspection.Outcome == ProvisioningReconciliationOutcomes.EffectExists
                    ? RuntimeProviderOutcome.Success("Managed storage is prepared.")
                    : inspection.Outcome == ProvisioningReconciliationOutcomes.EffectAbsent
                        ? RuntimeProviderOutcome.KnownFailure(ManagedStorageErrorCodes.PrepareFailed, "Managed storage could not be prepared.")
                        : RuntimeProviderOutcome.Unknown(ManagedStorageErrorCodes.ReconciliationAmbiguous, "Managed storage preparation outcome is unknown."));
            }
        }

        return Task.FromResult(RuntimeProviderOutcome.Success("Managed storage is prepared."));
    }

    public Task<ProvisioningReconciliationResult> InspectAsync(ValidatedManagedStorageTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        var outcomes = target.Entries.Select(entry => InspectEntry(target.DataRoot, entry.AbsolutePath).Outcome).ToArray();
        var outcome = outcomes.All(item => item == ProvisioningReconciliationOutcomes.EffectExists)
            ? ProvisioningReconciliationOutcomes.EffectExists
            : outcomes.All(item => item == ProvisioningReconciliationOutcomes.EffectAbsent)
                ? ProvisioningReconciliationOutcomes.EffectAbsent
                : ProvisioningReconciliationOutcomes.Ambiguous;
        return Task.FromResult(new ProvisioningReconciliationResult(outcome, outcome switch
        {
            ProvisioningReconciliationOutcomes.EffectExists => "Managed storage exists.",
            ProvisioningReconciliationOutcomes.EffectAbsent => "Managed storage is absent.",
            _ => "Managed storage state could not be proven safely."
        }));
    }

    private ProvisioningReconciliationResult InspectEntry(string root, string target)
    {
        try
        {
            var safety = InspectPathSafety(root, target);
            if (!safety.Safe)
                return new(ProvisioningReconciliationOutcomes.Ambiguous, "Managed storage state could not be proven safely.");
            return _directories.Exists(target)
                ? new(ProvisioningReconciliationOutcomes.EffectExists, "Managed storage exists.")
                : new(ProvisioningReconciliationOutcomes.EffectAbsent, "Managed storage is absent.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(ProvisioningReconciliationOutcomes.Ambiguous, "Managed storage state could not be proven safely.");
        }
    }

    private (bool Safe, string? Code) InspectPathSafety(string root, string target)
    {
        try
        {
            var contained = ManagedStoragePathBuilder.EnsureContained(root, target, "Managed storage target escaped the data root.");
            if (contained.Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return (false, ManagedStorageErrorCodes.TargetInvalid);
            for (var current = new DirectoryInfo(contained); current is not null; current = current.Parent)
            {
                if (_directories.Exists(current.FullName)
                    && (_directories.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                    return (false, ManagedStorageErrorCodes.TargetInvalid);
            }
            return (true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or StoragePlanningException)
        {
            return (false, ManagedStorageErrorCodes.ReconciliationAmbiguous);
        }
    }
}
