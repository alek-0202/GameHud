using Docker.DotNet.Models;
using GamesHud.Api.Docker.Contracts;

namespace GamesHud.Api.Docker.Services;

internal static class DockerContainerMapper
{
    public static ContainerResponse Map(ContainerListResponse container)
    {
        return new ContainerResponse(
            container.ID ?? string.Empty,
            NormalizeName(container.Names),
            container.Image ?? string.Empty,
            container.State ?? string.Empty,
            container.Status ?? string.Empty);
    }

    private static string NormalizeName(IList<string>? names)
    {
        var name = names?.FirstOrDefault() ?? string.Empty;

        return name.TrimStart('/');
    }
}
