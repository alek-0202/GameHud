using GamesHud.Api.GameServers.Services;
using GamesHud.Api.Palworld.Contracts;

namespace GamesHud.Api.Palworld.Services;

public sealed class PalworldAdminService : IPalworldAdminService
{
    private const int MaxMessageLength = 200;
    private const int MaxUserIdLength = 128;

    private readonly IGameServerRegistry _gameServerRegistry;
    private readonly IPalworldRestService _palworldRestService;

    public PalworldAdminService(
        IGameServerRegistry gameServerRegistry,
        IPalworldRestService palworldRestService)
    {
        _gameServerRegistry = gameServerRegistry;
        _palworldRestService = palworldRestService;
    }

    public async Task<PalworldAdminActionResponse> AnnounceAsync(
        string serverId,
        PalworldAnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        var message = NormalizeMessage(request.Message, required: true);
        var options = _gameServerRegistry.GetPalworldOptions(serverId);

        await _palworldRestService.AnnounceAsync(options.RestApi, message!, cancellationToken);

        return CreateResponse("Announcement sent.", "announce", "server");
    }

    public async Task<PalworldAdminActionResponse> KickAsync(
        string serverId,
        string userId,
        PalworldPlayerActionRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        ValidateConfirmation(request.ConfirmationText, $"KICK {normalizedUserId}");
        var message = NormalizeMessage(request.Message, required: false);
        var options = _gameServerRegistry.GetPalworldOptions(serverId);

        await _palworldRestService.KickAsync(options.RestApi, normalizedUserId, message, cancellationToken);

        return CreateResponse("Player kick requested.", "kick", normalizedUserId);
    }

    public async Task<PalworldAdminActionResponse> BanAsync(
        string serverId,
        string userId,
        PalworldPlayerActionRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        ValidateConfirmation(request.ConfirmationText, $"BAN {normalizedUserId}");
        var message = NormalizeMessage(request.Message, required: false);
        var options = _gameServerRegistry.GetPalworldOptions(serverId);

        await _palworldRestService.BanAsync(options.RestApi, normalizedUserId, message, cancellationToken);

        return CreateResponse("Player ban requested.", "ban", normalizedUserId);
    }

    public async Task<PalworldAdminActionResponse> UnbanAsync(
        string serverId,
        PalworldUnbanRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(request.UserId);
        ValidateConfirmation(request.ConfirmationText, $"UNBAN {normalizedUserId}");
        var options = _gameServerRegistry.GetPalworldOptions(serverId);

        await _palworldRestService.UnbanAsync(options.RestApi, normalizedUserId, cancellationToken);

        return CreateResponse("Player unban requested.", "unban", normalizedUserId);
    }

    private static string? NormalizeMessage(string? message, bool required)
    {
        var errors = new List<string>();
        var normalized = message?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            if (required)
            {
                errors.Add("Message is required.");
            }
        }
        else
        {
            if (normalized.Length > MaxMessageLength)
            {
                errors.Add($"Message must be {MaxMessageLength} characters or fewer.");
            }

            if (normalized.Contains('<') || normalized.Contains('>'))
            {
                errors.Add("Message cannot contain HTML-like angle brackets.");
            }

            if (normalized.Contains('\r') || normalized.Contains('\n'))
            {
                errors.Add("Message cannot contain line breaks.");
            }
        }

        if (errors.Count > 0)
        {
            throw new PalworldAdminValidationException(errors);
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeUserId(string? userId)
    {
        var normalized = userId?.Trim();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            errors.Add("User id is required.");
        }
        else
        {
            if (normalized.Length > MaxUserIdLength)
            {
                errors.Add($"User id must be {MaxUserIdLength} characters or fewer.");
            }

            if (normalized.Contains('/') || normalized.Contains('\\')
                || normalized.Contains('\r') || normalized.Contains('\n'))
            {
                errors.Add("User id contains unsupported characters.");
            }
        }

        if (errors.Count > 0)
        {
            throw new PalworldAdminValidationException(errors);
        }

        return normalized!;
    }

    private static void ValidateConfirmation(string? confirmationText, string expectedText)
    {
        if (!string.Equals(confirmationText?.Trim(), expectedText, StringComparison.Ordinal))
        {
            throw new PalworldAdminValidationException(
                [$"Confirmation text must be exactly '{expectedText}'."]);
        }
    }

    private static PalworldAdminActionResponse CreateResponse(
        string message,
        string action,
        string userId)
    {
        return new PalworldAdminActionResponse(
            message,
            action,
            userId,
            DateTimeOffset.UtcNow.ToString("O"));
    }
}
