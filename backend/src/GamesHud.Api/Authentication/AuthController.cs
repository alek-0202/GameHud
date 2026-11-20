using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Authentication;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAntiforgery antiforgery) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("csrf")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<AntiforgeryTokenResponse> Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new AntiforgeryTokenResponse(tokens.RequestToken!));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            return Unauthorized(InvalidCredentials());

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        var result = user is null
            ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signInManager.PasswordSignInAsync(user, request.Password, false, true);
        if (!result.Succeeded) return Unauthorized(InvalidCredentials());
        return Ok(new CurrentSessionResponse(true,
            new AuthenticatedUserResponse(user!.Id, user.Email ?? user.UserName ?? string.Empty)));
    }

    [Authorize]
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("me")]
    public async Task<ActionResult<CurrentSessionResponse>> Me()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(id))
            return Ok(new CurrentSessionResponse(false, null));
        var user = await userManager.FindByIdAsync(id);
        return user is null
            ? Ok(new CurrentSessionResponse(false, null))
            : Ok(new CurrentSessionResponse(true,
                new AuthenticatedUserResponse(user.Id, user.Email ?? user.UserName ?? string.Empty)));
    }

    private static AuthenticationErrorResponse InvalidCredentials() =>
        new("invalid_credentials", "The email or password is invalid.");
}
