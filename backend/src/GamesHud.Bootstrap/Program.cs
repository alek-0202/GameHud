using GamesHud.Api.Authentication;
using GamesHud.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Length != 0)
{
    Console.Error.WriteLine("This tool accepts no command-line arguments; run it interactively.");
    return BootstrapExitCodes.Usage;
}

try
{
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Services.AddGamesHudIdentityPersistence(builder.Configuration);
    builder.Services.AddSingleton<IBootstrapConsole, SystemBootstrapConsole>();
    builder.Services.AddScoped<FirstAccountBootstrapCommand>();

    using var host = builder.Build();
    await using var scope = host.Services.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<FirstAccountBootstrapCommand>()
        .RunAsync(CancellationToken.None);
}
catch (Exception)
{
    Console.Error.WriteLine("Bootstrap failed safely; no account was confirmed as created.");
    return BootstrapExitCodes.Failure;
}
