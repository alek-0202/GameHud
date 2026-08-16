using System.Text.Json.Serialization;

namespace GamesHud.Api.Docker.Contracts;

public sealed class ContainerLogsResponse
{
    [JsonConstructor]
    public ContainerLogsResponse(
        string containerId,
        IReadOnlyCollection<string> lines,
        IReadOnlyCollection<ContainerLogEntryResponse> entries,
        string retrievedAt)
    {
        ContainerId = containerId;
        Lines = lines;
        Entries = entries;
        RetrievedAt = retrievedAt;
    }

    public ContainerLogsResponse(
        string containerId,
        IReadOnlyCollection<string> lines,
        string retrievedAt)
        : this(
            containerId,
            lines,
            lines.Select(line => new ContainerLogEntryResponse(line, "unknown", "default", null)).ToArray(),
            retrievedAt)
    {
    }

    public string ContainerId { get; }

    public IReadOnlyCollection<string> Lines { get; }

    public IReadOnlyCollection<ContainerLogEntryResponse> Entries { get; }

    public string RetrievedAt { get; }
}

public sealed record ContainerLogEntryResponse(
    string Message,
    string Stream,
    string Severity,
    string? Timestamp);
