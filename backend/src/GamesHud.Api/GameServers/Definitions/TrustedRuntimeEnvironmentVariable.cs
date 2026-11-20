namespace GamesHud.Api.GameServers.Definitions;

public sealed record TrustedRuntimeEnvironmentVariable
{
    internal TrustedRuntimeEnvironmentVariable(string runtimeType, string name, string value)
    {
        RuntimeType = runtimeType;
        Name = name;
        Value = value;
    }

    public string RuntimeType { get; }

    public string Name { get; }

    public string Value { get; }
}
