namespace GamesHud.Api.Metrics.Services;

public sealed class InMemoryMetricsHistoryStore : IMetricsHistoryStore
{
    private readonly object _lock = new();
    private readonly List<MetricSnapshot> _snapshots = new();

    public void Add(MetricSnapshot snapshot, TimeSpan retention)
    {
        lock (_lock)
        {
            _snapshots.Add(snapshot);
            var cutoff = snapshot.Timestamp - retention;
            _snapshots.RemoveAll(item => item.Timestamp < cutoff);
        }
    }

    public IReadOnlyCollection<MetricSnapshot> GetSince(DateTimeOffset since)
    {
        lock (_lock)
        {
            return _snapshots
                .Where(snapshot => snapshot.Timestamp >= since)
                .OrderBy(snapshot => snapshot.Timestamp)
                .ToArray();
        }
    }
}
