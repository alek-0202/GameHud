using System.Security.Claims;

namespace GamesHud.Api.Authentication;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true && UserId is not null;
    public string? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
