using System.ComponentModel.DataAnnotations;
using System.Data;
using GamesHud.Api.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.Authentication;

public static class FirstAccountBootstrapStatuses
{
    public const string Created = "created";
    public const string AlreadyInitialized = "already_initialized";
    public const string InvalidInput = "invalid_input";
    public const string ValidationFailed = "validation_failed";
}

public sealed record FirstAccountBootstrapResult(
    string Status,
    IReadOnlyCollection<string> ErrorCodes)
{
    public bool Succeeded => Status == FirstAccountBootstrapStatuses.Created;
}

public interface IFirstAccountBootstrapService
{
    Task<FirstAccountBootstrapResult> CreateFirstAsync(
        string email,
        string password,
        CancellationToken cancellationToken);
}

public sealed class FirstAccountBootstrapService(
    UserManager<ApplicationUser> users,
    GamesHudDbContext database) : IFirstAccountBootstrapService
{
    public async Task<FirstAccountBootstrapResult> CreateFirstAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedEmail = email?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail)
            || !new EmailAddressAttribute().IsValid(normalizedEmail)
            || string.IsNullOrEmpty(password))
        {
            return new(FirstAccountBootstrapStatuses.InvalidInput, ["invalid_account"]);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (await users.Users.AsNoTracking().AnyAsync(cancellationToken))
        {
            return new(FirstAccountBootstrapStatuses.AlreadyInitialized, []);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = normalizedEmail,
            Email = normalizedEmail
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return new(
                FirstAccountBootstrapStatuses.ValidationFailed,
                result.Errors.Select(error => error.Code).Distinct(StringComparer.Ordinal).ToArray());
        }

        await transaction.CommitAsync(cancellationToken);
        return new(FirstAccountBootstrapStatuses.Created, []);
    }
}
