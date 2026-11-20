using GamesHud.Api.Authentication;

namespace GamesHud.Bootstrap;

public static class BootstrapExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int AlreadyInitialized = 2;
    public const int InvalidInput = 3;
    public const int IdentityValidationFailed = 4;
    public const int Usage = 64;
    public const int Cancelled = 130;
}

public sealed class FirstAccountBootstrapCommand(
    IFirstAccountBootstrapService bootstrap,
    IBootstrapConsole console)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (console.IsInputRedirected)
        {
            console.WriteLine("Bootstrap requires an interactive terminal; redirected input is not accepted.");
            return BootstrapExitCodes.Usage;
        }

        var email = console.ReadLine("Email (also used as username): ")?.Trim() ?? string.Empty;
        var password = console.ReadSecret("Password: ");
        var confirmation = console.ReadSecret("Confirm password: ");
        try
        {
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            {
                console.WriteLine("Bootstrap refused: password confirmation does not match.");
                return BootstrapExitCodes.InvalidInput;
            }

            var result = await bootstrap.CreateFirstAsync(email, password, cancellationToken);
            return WriteResult(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            console.WriteLine("Bootstrap was cancelled; no account was confirmed as created.");
            return BootstrapExitCodes.Cancelled;
        }
        catch (Exception)
        {
            console.WriteLine("Bootstrap failed safely; no account was confirmed as created.");
            return BootstrapExitCodes.Failure;
        }
        finally
        {
            password = string.Empty;
            confirmation = string.Empty;
        }
    }

    private int WriteResult(FirstAccountBootstrapResult result)
    {
        switch (result.Status)
        {
            case FirstAccountBootstrapStatuses.Created:
                console.WriteLine("The first GamesHud account was created.");
                return BootstrapExitCodes.Success;
            case FirstAccountBootstrapStatuses.AlreadyInitialized:
                console.WriteLine("Bootstrap refused: a GamesHud account already exists.");
                return BootstrapExitCodes.AlreadyInitialized;
            case FirstAccountBootstrapStatuses.InvalidInput:
                console.WriteLine("Bootstrap refused: enter a valid email and a non-empty password.");
                return BootstrapExitCodes.InvalidInput;
            case FirstAccountBootstrapStatuses.ValidationFailed:
                console.WriteLine("The account was not created because the credentials do not satisfy the configured Identity policy.");
                return BootstrapExitCodes.IdentityValidationFailed;
            default:
                console.WriteLine("Bootstrap failed safely; no account was confirmed as created.");
                return BootstrapExitCodes.Failure;
        }
    }
}
