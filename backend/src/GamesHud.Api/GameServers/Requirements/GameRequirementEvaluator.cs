using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.HostCapabilities.Models;

namespace GamesHud.Api.GameServers.Requirements;

public sealed class GameRequirementEvaluator : IGameRequirementEvaluator
{
    public GameCompatibilityAssessment Evaluate(
        GameDefinition definition,
        HostCapabilitySnapshot hostCapabilities)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(hostCapabilities);

        if (definition.Requirements is null)
        {
            var check = new GameCompatibilityCheck(
                "requirements",
                "Requirements",
                "Declared game requirements",
                "No requirements declared",
                RequirementCheckStatuses.Unknown,
                "GamesHud does not have requirements for this game yet.");

            return new GameCompatibilityAssessment(
                definition.GameId,
                definition.DisplayName,
                GameCompatibilityStatuses.Unknown,
                [check],
                [],
                [new GameCompatibilityIssue(
                    "requirements_unknown",
                    RequirementIssueSeverities.Warning,
                    "GamesHud does not have requirements for this game yet.")]);
        }

        var requirements = definition.Requirements;
        var checks = new List<GameCompatibilityCheck>
        {
            EvaluateOperatingSystem(requirements, hostCapabilities),
            EvaluateArchitecture(requirements, hostCapabilities),
            EvaluateCpu(requirements, hostCapabilities),
            EvaluateMemory(requirements, hostCapabilities),
            EvaluateStorage(requirements, hostCapabilities)
        };

        checks.AddRange(requirements.RequiredRuntimes.Select(runtime =>
            EvaluateRuntime(runtime, hostCapabilities)));

        checks.RemoveAll(check => check.Id == "not_declared");

        var blockingIssues = checks
            .Where(check => check.Status == RequirementCheckStatuses.Failed)
            .Select(check => new GameCompatibilityIssue(
                $"{check.Id}_failed",
                RequirementIssueSeverities.Blocking,
                check.Message))
            .ToArray();
        var warnings = checks
            .Where(check => check.Status is RequirementCheckStatuses.Warning or RequirementCheckStatuses.Unknown)
            .Select(check => new GameCompatibilityIssue(
                check.Status == RequirementCheckStatuses.Unknown
                    ? $"{check.Id}_unknown"
                    : $"{check.Id}_warning",
                RequirementIssueSeverities.Warning,
                check.Message))
            .ToArray();

        return new GameCompatibilityAssessment(
            definition.GameId,
            definition.DisplayName,
            CalculateStatus(blockingIssues, warnings, checks),
            checks,
            blockingIssues,
            warnings);
    }

    private static GameCompatibilityCheck EvaluateOperatingSystem(
        GameRequirements requirements,
        HostCapabilitySnapshot hostCapabilities)
    {
        if (requirements.SupportedOperatingSystems.Count == 0)
        {
            return NotDeclared();
        }

        var detected = hostCapabilities.OperatingSystem.Family;
        var required = string.Join(", ", requirements.SupportedOperatingSystems.Select(FormatIdentifier));

        if (requirements.SupportedOperatingSystems.Contains(detected, StringComparer.Ordinal))
        {
            return Passed(
                "operating_system",
                "Operating System",
                required,
                FormatIdentifier(detected),
                $"{FormatIdentifier(detected)} is supported.");
        }

        return Failed(
            "operating_system",
            "Operating System",
            required,
            FormatIdentifier(detected),
            $"{FormatIdentifier(detected)} is not listed as a supported host operating system.");
    }

    private static GameCompatibilityCheck EvaluateArchitecture(
        GameRequirements requirements,
        HostCapabilitySnapshot hostCapabilities)
    {
        if (requirements.SupportedArchitectures.Count == 0)
        {
            return NotDeclared();
        }

        var detected = hostCapabilities.OperatingSystem.Architecture;
        var required = string.Join(", ", requirements.SupportedArchitectures);

        if (requirements.SupportedArchitectures.Contains(detected, StringComparer.Ordinal))
        {
            return Passed(
                "architecture",
                "Architecture",
                required,
                detected,
                $"{detected} is supported.");
        }

        return Failed(
            "architecture",
            "Architecture",
            required,
            detected,
            $"{detected} is not listed as a supported host architecture.");
    }

    private static GameCompatibilityCheck EvaluateCpu(
        GameRequirements requirements,
        HostCapabilitySnapshot hostCapabilities)
    {
        if (requirements.MinimumLogicalProcessors is null
            && requirements.RecommendedLogicalProcessors is null)
        {
            return NotDeclared();
        }

        var detected = hostCapabilities.Cpu.LogicalProcessors;
        var required = FormatCpuRequirement(
            requirements.MinimumLogicalProcessors,
            requirements.RecommendedLogicalProcessors);

        if (requirements.MinimumLogicalProcessors is not null
            && detected < requirements.MinimumLogicalProcessors.Value)
        {
            return Failed(
                "cpu",
                "CPU",
                required,
                $"{detected} logical processors",
                "The host does not meet the minimum CPU requirement.");
        }

        if (requirements.RecommendedLogicalProcessors is not null
            && detected < requirements.RecommendedLogicalProcessors.Value)
        {
            return Warning(
                "cpu",
                "CPU",
                required,
                $"{detected} logical processors",
                "The host meets the minimum CPU requirement but is below the recommendation.");
        }

        return Passed(
            "cpu",
            "CPU",
            required,
            $"{detected} logical processors",
            "The host meets the CPU requirement.");
    }

    private static GameCompatibilityCheck EvaluateMemory(
        GameRequirements requirements,
        HostCapabilitySnapshot hostCapabilities)
    {
        if (requirements.Memory is null)
        {
            return NotDeclared();
        }

        if (hostCapabilities.Memory.Status != HostCapabilityStatuses.Available
            || hostCapabilities.Memory.TotalBytes is null)
        {
            return Unknown(
                "memory",
                "Memory",
                FormatByteRequirement(requirements.Memory),
                "Unavailable",
                "Memory could not be detected on this host.");
        }

        var detected = hostCapabilities.Memory.TotalBytes.Value;

        if (requirements.Memory.MinimumBytes is not null
            && detected < requirements.Memory.MinimumBytes.Value)
        {
            return Failed(
                "memory",
                "Memory",
                FormatByteRequirement(requirements.Memory),
                FormatBytes(detected),
                "The host does not meet the minimum memory requirement.");
        }

        if (requirements.Memory.RecommendedBytes is not null
            && detected < requirements.Memory.RecommendedBytes.Value)
        {
            return Warning(
                "memory",
                "Memory",
                FormatByteRequirement(requirements.Memory),
                FormatBytes(detected),
                "The host meets the minimum memory requirement but is below the recommendation.");
        }

        return Passed(
            "memory",
            "Memory",
            FormatByteRequirement(requirements.Memory),
            FormatBytes(detected),
            "The host meets the memory requirement.");
    }

    private static GameCompatibilityCheck EvaluateStorage(
        GameRequirements requirements,
        HostCapabilitySnapshot hostCapabilities)
    {
        if (requirements.Storage is null)
        {
            return NotDeclared();
        }

        if (hostCapabilities.Storage.Status != HostCapabilityStatuses.Available
            || hostCapabilities.Storage.AvailableBytes is null)
        {
            return Unknown(
                "storage",
                "Storage",
                FormatByteRequirement(requirements.Storage),
                "Unavailable",
                "Storage could not be detected on this host.");
        }

        var detected = hostCapabilities.Storage.AvailableBytes.Value;

        if (requirements.Storage.MinimumBytes is not null
            && detected < requirements.Storage.MinimumBytes.Value)
        {
            return Failed(
                "storage",
                "Storage",
                FormatByteRequirement(requirements.Storage),
                FormatBytes(detected),
                "The host does not meet the minimum free storage requirement.");
        }

        if (requirements.Storage.RecommendedBytes is not null
            && detected < requirements.Storage.RecommendedBytes.Value)
        {
            return Warning(
                "storage",
                "Storage",
                FormatByteRequirement(requirements.Storage),
                FormatBytes(detected),
                "The host meets the minimum storage requirement but is below the recommendation.");
        }

        return Passed(
            "storage",
            "Storage",
            FormatByteRequirement(requirements.Storage),
            FormatBytes(detected),
            "The host meets the storage requirement.");
    }

    private static GameCompatibilityCheck EvaluateRuntime(
        string runtimeId,
        HostCapabilitySnapshot hostCapabilities)
    {
        var runtime = hostCapabilities.Runtimes.FirstOrDefault(candidate =>
            candidate.Id.Equals(runtimeId, StringComparison.Ordinal));

        if (runtime is null)
        {
            return Failed(
                $"runtime_{runtimeId}",
                FormatIdentifier(runtimeId),
                "Runtime available",
                "Missing",
                $"{FormatIdentifier(runtimeId)} is required but was not detected.");
        }

        if (runtime.Status == HostCapabilityStatuses.Available && runtime.Reachable)
        {
            return Passed(
                $"runtime_{runtimeId}",
                runtime.DisplayName,
                "Runtime available",
                "Ready",
                $"{runtime.DisplayName} is ready.");
        }

        return Failed(
            $"runtime_{runtimeId}",
            runtime.DisplayName,
            "Runtime available",
            FormatIdentifier(runtime.Status),
            $"{runtime.DisplayName} is required but is not available.");
    }

    private static string CalculateStatus(
        IReadOnlyCollection<GameCompatibilityIssue> blockingIssues,
        IReadOnlyCollection<GameCompatibilityIssue> warnings,
        IReadOnlyCollection<GameCompatibilityCheck> checks)
    {
        if (blockingIssues.Count > 0)
        {
            return GameCompatibilityStatuses.Incompatible;
        }

        if (checks.Any(check => check.Status == RequirementCheckStatuses.Unknown))
        {
            return GameCompatibilityStatuses.Unknown;
        }

        return warnings.Count > 0
            ? GameCompatibilityStatuses.CompatibleWithWarnings
            : GameCompatibilityStatuses.Compatible;
    }

    private static GameCompatibilityCheck NotDeclared()
    {
        return new GameCompatibilityCheck(
            "not_declared",
            "Not Declared",
            string.Empty,
            string.Empty,
            RequirementCheckStatuses.Unknown,
            string.Empty);
    }

    private static GameCompatibilityCheck Passed(
        string id,
        string label,
        string required,
        string detected,
        string message)
    {
        return new GameCompatibilityCheck(
            id,
            label,
            required,
            detected,
            RequirementCheckStatuses.Passed,
            message);
    }

    private static GameCompatibilityCheck Warning(
        string id,
        string label,
        string required,
        string detected,
        string message)
    {
        return new GameCompatibilityCheck(
            id,
            label,
            required,
            detected,
            RequirementCheckStatuses.Warning,
            message);
    }

    private static GameCompatibilityCheck Failed(
        string id,
        string label,
        string required,
        string detected,
        string message)
    {
        return new GameCompatibilityCheck(
            id,
            label,
            required,
            detected,
            RequirementCheckStatuses.Failed,
            message);
    }

    private static GameCompatibilityCheck Unknown(
        string id,
        string label,
        string required,
        string detected,
        string message)
    {
        return new GameCompatibilityCheck(
            id,
            label,
            required,
            detected,
            RequirementCheckStatuses.Unknown,
            message);
    }

    private static string FormatByteRequirement(ByteRequirement requirement)
    {
        return requirement switch
        {
            { MinimumBytes: not null, RecommendedBytes: not null } =>
                $"{FormatBytes(requirement.MinimumBytes.Value)} minimum, {FormatBytes(requirement.RecommendedBytes.Value)} recommended",
            { MinimumBytes: not null } => $"{FormatBytes(requirement.MinimumBytes.Value)} minimum",
            { RecommendedBytes: not null } => $"{FormatBytes(requirement.RecommendedBytes.Value)} recommended",
            _ => "Not specified"
        };
    }

    private static string FormatCpuRequirement(int? minimum, int? recommended)
    {
        return (minimum, recommended) switch
        {
            (not null, not null) => $"{minimum.Value} minimum, {recommended.Value} recommended logical processors",
            (not null, null) => $"{minimum.Value} minimum logical processors",
            (null, not null) => $"{recommended.Value} recommended logical processors",
            _ => "Not specified"
        };
    }

    private static string FormatBytes(ulong bytes)
    {
        var gibibytes = bytes / (double)GameRequirementBytes.Gibibytes(1);

        return $"{gibibytes:0.#} GB";
    }

    private static string FormatIdentifier(string value)
    {
        return string.Join(
            " ",
            value
                .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => $"{char.ToUpperInvariant(part[0])}{part[1..]}"));
    }
}
