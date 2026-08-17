namespace GamesHud.Api.Palworld.Services;

public sealed class PalworldAdminValidationException : Exception
{
    public PalworldAdminValidationException(IEnumerable<string> errors)
        : base("Palworld admin action request is invalid.")
    {
        Errors = errors.ToArray();
    }

    public IReadOnlyCollection<string> Errors { get; }
}
