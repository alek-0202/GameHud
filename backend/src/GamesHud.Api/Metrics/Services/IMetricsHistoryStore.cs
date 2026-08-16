namespace GamesHud.Api.Metrics.Services;

public interface IMetricsHistoryStore
{
    void Add(MetricSnapshot snapshot, TimeSpan retention);

    IReadOnlyCollection<MetricSnapshot> GetSince(DateTimeOffset since);
}
