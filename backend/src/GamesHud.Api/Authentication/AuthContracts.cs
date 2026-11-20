namespace GamesHud.Api.Authentication;

public sealed record LoginRequest(string? Email, string? Password);
public sealed record AuthenticatedUserResponse(string Id, string Email);
public sealed record CurrentSessionResponse(bool Authenticated, AuthenticatedUserResponse? User);
public sealed record AntiforgeryTokenResponse(string RequestToken);
public sealed record AuthenticationErrorResponse(string Code, string Message);
