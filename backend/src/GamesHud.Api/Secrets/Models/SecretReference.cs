namespace GamesHud.Api.Secrets.Models;

public sealed record SecretReference(SecretId Id)
{
    public override string ToString()
    {
        return $"secret_ref:{Id.Value}";
    }
}
