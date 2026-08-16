using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GamesHud.Api.Palworld.Configuration;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Palworld.Services;

public sealed class PalworldRestService : IPalworldRestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<PalworldOptions> _options;

    public PalworldRestService(
        HttpClient httpClient,
        IOptions<PalworldOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<PalworldRestInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<PalworldRestInfoDto>("info", cancellationToken);

        return new PalworldRestInfo(
            response.Version,
            response.ServerName,
            response.Description,
            response.WorldGuid);
    }

    public async Task<PalworldRestPlayers> GetPlayersAsync(CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<PalworldRestPlayersDto>("players", cancellationToken);

        return new PalworldRestPlayers(
            response.Players
                .Select(player => new PalworldRestPlayer(
                    player.Name,
                    player.AccountName,
                    player.PlayerId,
                    player.UserId,
                    player.Ip,
                    player.Ping,
                    player.LocationX,
                    player.LocationY,
                    player.Level,
                    player.BuildingCount))
                .ToArray());
    }

    public async Task<PalworldRestSettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<PalworldRestSettingsDto>("settings", cancellationToken);

        return new PalworldRestSettings(
            response.ServerPlayerMaxNum,
            response.ServerName,
            response.ServerDescription);
    }

    public async Task<PalworldRestMetrics> GetMetricsAsync(CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<PalworldRestMetricsDto>("metrics", cancellationToken);

        return new PalworldRestMetrics(
            response.ServerFps,
            response.CurrentPlayerNum,
            response.ServerFrameTime,
            response.MaxPlayerNum,
            response.Uptime,
            response.BaseCampNum,
            response.Days);
    }

    private async Task<TResponse> GetJsonAsync<TResponse>(
        string endpoint,
        CancellationToken cancellationToken)
    {
        var restOptions = ResolveRestOptions();
        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(restOptions.TimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Get, ResolveEndpoint(restOptions.BaseUrl, endpoint));

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{restOptions.Username}:{restOptions.Password}"))));

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCancellationTokenSource.Token);

            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                throw new PalworldRestUnauthorizedException();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new PalworldRestUnavailableException(
                    $"Palworld REST API returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCancellationTokenSource.Token);
            var result = await JsonSerializer.DeserializeAsync<TResponse>(
                stream,
                JsonOptions,
                timeoutCancellationTokenSource.Token);

            return result
                ?? throw new PalworldRestMalformedResponseException("Palworld REST API returned an empty response.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PalworldRestUnavailableException("Palworld REST API request timed out.", exception);
        }
        catch (JsonException exception)
        {
            throw new PalworldRestMalformedResponseException("Palworld REST API returned malformed JSON.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new PalworldRestUnavailableException("Palworld REST API is unavailable.", exception);
        }
    }

    private ResolvedRestOptions ResolveRestOptions()
    {
        var restOptions = _options.Value.RestApi;

        if (string.IsNullOrWhiteSpace(restOptions.BaseUrl))
        {
            throw new PalworldRestConfigurationException("Palworld REST API base URL is not configured.");
        }

        if (!Uri.TryCreate(restOptions.BaseUrl.Trim(), UriKind.Absolute, out var baseUrl))
        {
            throw new PalworldRestConfigurationException("Palworld REST API base URL is invalid.");
        }

        if (string.IsNullOrWhiteSpace(restOptions.Username)
            || string.IsNullOrWhiteSpace(restOptions.Password))
        {
            throw new PalworldRestConfigurationException("Palworld REST API credentials are not configured.");
        }

        var timeoutSeconds = restOptions.TimeoutSeconds is >= 1 and <= 30
            ? restOptions.TimeoutSeconds
            : 5;

        return new ResolvedRestOptions(
            baseUrl,
            restOptions.Username,
            restOptions.Password,
            timeoutSeconds);
    }

    private static Uri ResolveEndpoint(Uri baseUrl, string endpoint)
    {
        var normalizedBaseUrl = baseUrl.ToString().TrimEnd('/');
        var normalizedPath = baseUrl.AbsolutePath.TrimEnd('/');
        var suffix = normalizedPath.EndsWith("/v1/api", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : $"v1/api/{endpoint}";

        return new Uri($"{normalizedBaseUrl}/{suffix}");
    }

    private sealed record ResolvedRestOptions(
        Uri BaseUrl,
        string Username,
        string Password,
        int TimeoutSeconds);

    private sealed record PalworldRestInfoDto(
        string? Version,
        [property: JsonPropertyName("servername")] string? ServerName,
        string? Description,
        [property: JsonPropertyName("worldguid")] string? WorldGuid);

    private sealed record PalworldRestPlayersDto(
        IReadOnlyCollection<PalworldRestPlayerDto> Players);

    private sealed record PalworldRestPlayerDto(
        string? Name,
        string? AccountName,
        string? PlayerId,
        string? UserId,
        string? Ip,
        double? Ping,
        [property: JsonPropertyName("location_x")] double? LocationX,
        [property: JsonPropertyName("location_y")] double? LocationY,
        int? Level,
        [property: JsonPropertyName("building_count")] int? BuildingCount);

    private sealed record PalworldRestSettingsDto(
        int? ServerPlayerMaxNum,
        string? ServerName,
        string? ServerDescription);

    private sealed record PalworldRestMetricsDto(
        [property: JsonPropertyName("serverfps")] int? ServerFps,
        [property: JsonPropertyName("currentplayernum")] int? CurrentPlayerNum,
        [property: JsonPropertyName("serverframetime")] double? ServerFrameTime,
        [property: JsonPropertyName("maxplayernum")] int? MaxPlayerNum,
        [property: JsonPropertyName("uptime")] int? Uptime,
        [property: JsonPropertyName("basecampnum")] int? BaseCampNum,
        [property: JsonPropertyName("days")] int? Days);
}
