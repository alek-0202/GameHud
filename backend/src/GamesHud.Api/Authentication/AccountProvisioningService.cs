using Microsoft.AspNetCore.Identity;

namespace GamesHud.Api.Authentication;

public sealed record AccountProvisioningResult(bool Succeeded, string? UserId, IReadOnlyCollection<string> ErrorCodes);

public interface IAccountProvisioningService
{
    Task<AccountProvisioningResult> CreateAsync(string email, string password, CancellationToken cancellationToken);
}

public sealed class AccountProvisioningService(UserManager<ApplicationUser> users) : IAccountProvisioningService
{
    public async Task<AccountProvisioningResult> CreateAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedEmail = email?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrEmpty(password))
            return new(false, null, ["invalid_account"]);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = normalizedEmail,
            Email = normalizedEmail
        };
        var result = await users.CreateAsync(user, password);
        return result.Succeeded
            ? new(true, user.Id, [])
            : new(false, null, result.Errors.Select(error => error.Code).ToArray());
    }
}
