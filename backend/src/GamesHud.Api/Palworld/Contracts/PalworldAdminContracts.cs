namespace GamesHud.Api.Palworld.Contracts;

public sealed record PalworldAnnouncementRequest(
    string Message);

public sealed record PalworldPlayerActionRequest(
    string ConfirmationText,
    string? Message);

public sealed record PalworldUnbanRequest(
    string UserId,
    string ConfirmationText);

public sealed record PalworldAdminActionResponse(
    string Message,
    string Action,
    string UserId,
    string CompletedAt);
