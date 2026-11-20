using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamesHud.Api.GameServers.Artifacts;
using GamesHud.Api.GameServers.Configuration;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Runtime;

namespace GamesHud.Api.Tests;

public sealed class MinecraftManagedProvisioningTests
{
    [Fact]
    public void PipelineIsVersionedWithoutChangingLegacySequences()
    {
        Assert.Equal("gh09-v1", ProvisioningPipelines.Legacy.Version);
        Assert.Equal("gh15-v2", ProvisioningPipelines.ImageAcquisition.Version);
        Assert.Equal("gh-mc01-v1", ProvisioningPipelines.MinecraftDumbMode.Version);
        Assert.Equal([
            ProvisioningStepIds.PrepareStorage,
            ProvisioningStepIds.AcquireImage,
            ProvisioningStepIds.AcquireGameArtifact,
            ProvisioningStepIds.ConfigureGame,
            ProvisioningStepIds.CreateRuntime,
            ProvisioningStepIds.StartRuntime,
            ProvisioningStepIds.VerifyHealth,
            ProvisioningStepIds.Complete
        ], ProvisioningPipelines.MinecraftDumbMode.Steps.OrderBy(step => step.Sequence)
            .Select(step => step.Id).ToArray());
        Assert.Equal(ProvisioningPipeline.Steps, ProvisioningPipelines.Legacy.Steps);
        Assert.Equal(ProvisioningStepIds.AcquireImage,
            ProvisioningPipelines.ImageAcquisition.Steps.Single(step => step.Sequence == 6).Id);
    }

    [Fact]
    public void DefinitionPinsApprovedArtifactImageAndPlatform()
    {
        var definition = new MinecraftGameDefinition();
        var image = Assert.Single(definition.RuntimeImages);

        Assert.Equal("minecraft", definition.GameId.Value);
        Assert.Equal("ghcr.io/itzg/minecraft-server@sha256:633aab667752905d540e7723690ec0dcb19584492219ef074dd934b59e9d23a7", image.Reference);
        Assert.Equal("linux", image.Platform!.OperatingSystem);
        Assert.Equal("amd64", image.Platform.Architecture);
        Assert.Equal(25565, Assert.Single(definition.Ports).DefaultPort.Number);
        Assert.Equal("4478a08dbcb57708f7a2fb29f4db51ef44e65806e9b8b618a6ec770dd0458d9e", LunchtimeArtifactManifest.Sha256);
    }

    [Fact]
    public void DurableConfigurationContainsAllChoicesAndRejectsTampering()
    {
        var codec = new MinecraftProvisioningConfigurationCodec();
        var durable = codec.CreateInitial(" Lunch ", 8192, 25570, true);
        var value = Assert.IsType<MinecraftInitialProvisioningConfiguration>(durable.TypedValue);

        Assert.Equal(MinecraftProvisioningConfigurationCodec.InitialConfigurationKind, durable.ConfigurationKind);
        Assert.Equal(1, durable.SchemaVersion);
        Assert.Equal("Lunch", value.ServerName);
        Assert.Equal(8192, value.JavaMemoryMiB);
        Assert.Equal(9216, value.ContainerMemoryMiB);
        Assert.Equal(25570, value.HostPort);
        Assert.True(value.EulaAccepted);
        Assert.Equal(LunchtimeArtifactManifest.CurseForgeFileId, value.CurseForgeFileId);

        var tampered = value with { ArtifactSha256 = new string('0', 64) };
        Assert.Throws<GameConfigurationException>(() => codec.Deserialize(
            "minecraft", codec.ConfigurationKind, codec.SchemaVersion,
            JsonSerializer.Serialize(tampered, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Assert.Throws<GameConfigurationException>(() => codec.CreateInitial("Lunch", 4096, 25565, true));
        Assert.Throws<GameConfigurationException>(() => codec.CreateInitial("Lunch", 6144, 25565, false));
    }

    [Fact]
    public void JavaHeapAndContainerLimitRemainSeparateAndPinned()
    {
        var intent = Assert.IsType<MinecraftInitialProvisioningConfiguration>(
            new MinecraftProvisioningConfigurationCodec().CreateInitial("Lunch", 8192, 25565, true).TypedValue);
        var environment = MinecraftRuntimeConfiguration.CreateEnvironment(intent)
            .ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
        var resources = MinecraftRuntimeConfiguration.CreateResources(intent, 2);

        Assert.Equal("8192M", environment["MEMORY"]);
        Assert.Equal("FORGE", environment["TYPE"]);
        Assert.Equal("1.20.1", environment["VERSION"]);
        Assert.Equal("47.4.29", environment["FORGE_VERSION"]);
        Assert.Equal("TRUE", environment["EULA"]);
        Assert.Equal(9UL * 1024 * 1024 * 1024, resources.MemoryBytes);
        Assert.Equal(2, resources.CpuCount);
    }

    [Fact]
    public async Task AcquisitionStreamsChecksumsPublishesAtomicallyAndIsIdempotent()
    {
        var zip = CreatePackZip();
        var handler = new StaticContentHandler(zip);
        var service = new MinecraftArtifactAcquisitionService(new HttpClient(handler));
        var intent = TestIntent(zip);
        var root = NewTempRoot();
        var target = Path.Combine(root, "data");
        try
        {
            Directory.CreateDirectory(target);
            var first = await service.AcquireAsync(target, "op-1", intent, default);
            var second = await service.AcquireAsync(target, "op-1", intent, default);
            var recovered = await service.InspectAsync(target, "op-1", intent, default);

            Assert.True(first.Succeeded, $"{first.SafeCode}: {first.SafeMessage}");
            Assert.True(second.Succeeded, $"{second.SafeCode}: {second.SafeMessage}");
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(ProvisioningReconciliationOutcomes.EffectExists, recovered.Outcome);
            Assert.True(File.Exists(Path.Combine(target, "server.properties")));
            Assert.True(File.Exists(Path.Combine(target, MinecraftArtifactAcquisitionService.MarkerFileName)));
            Assert.False(File.Exists(Path.Combine(target, "start.sh")));
            Assert.False(Directory.Exists(Path.Combine(target, "old-mods-backup")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root, "*.staging-*"));
            Assert.Empty(Directory.EnumerateFiles(root, "*.download-*.zip"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("config\\escape.txt")]
    public async Task AcquisitionRejectsUnsafeZipPaths(string unsafeRelativePath)
    {
        var zip = CreatePackZip(unsafeRelativePath);
        var service = new MinecraftArtifactAcquisitionService(new HttpClient(new StaticContentHandler(zip)));
        var root = NewTempRoot();
        try
        {
            var result = await service.AcquireAsync(Path.Combine(root, "data"), "op-safe", TestIntent(zip), default);
            Assert.False(result.Succeeded);
            Assert.Equal(MinecraftArtifactErrorCodes.ArchiveUnsafe, result.SafeCode);
            Assert.False(File.Exists(Path.Combine(root, "escape.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task AcquisitionRejectsChecksumMismatchWithoutPublishing()
    {
        var zip = CreatePackZip();
        var service = new MinecraftArtifactAcquisitionService(new HttpClient(new StaticContentHandler(zip)));
        var root = NewTempRoot();
        try
        {
            var result = await service.AcquireAsync(Path.Combine(root, "data"), "op-hash",
                TestIntent(zip) with { ArtifactSha256 = new string('0', 64) }, default);
            Assert.False(result.Succeeded);
            Assert.Equal(MinecraftArtifactErrorCodes.IdentityMismatch, result.SafeCode);
            Assert.False(Directory.Exists(Path.Combine(root, "data")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task AcquisitionRejectsDuplicatePathsAndLinks()
    {
        var duplicateZip = CreatePackZip(customize: archive =>
        {
            WriteEntry(archive, "pack/config/Duplicate.txt", "one");
            WriteEntry(archive, "pack/config/duplicate.txt", "two");
        });
        var linkZip = CreatePackZip(customize: archive =>
        {
            var link = archive.CreateEntry("pack/config/link");
            link.ExternalAttributes = unchecked((int)0xA000 << 16);
            using var writer = new StreamWriter(link.Open());
            writer.Write("target");
        });

        foreach (var zip in new[] { duplicateZip, linkZip })
        {
            var service = new MinecraftArtifactAcquisitionService(new HttpClient(new StaticContentHandler(zip)));
            var root = NewTempRoot();
            try
            {
                var result = await service.AcquireAsync(Path.Combine(root, "data"), "op-unsafe", TestIntent(zip), default);
                Assert.False(result.Succeeded);
                Assert.Equal(MinecraftArtifactErrorCodes.ArchiveUnsafe, result.SafeCode);
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    [Fact]
    public async Task AcquisitionRejectsExcessiveCompressionRatio()
    {
        var zip = CreatePackZip(customize: archive =>
        {
            var entry = archive.CreateEntry("pack/config/bomb.bin", CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(new byte[1024 * 1024]);
        });
        var service = new MinecraftArtifactAcquisitionService(new HttpClient(new StaticContentHandler(zip)));
        var root = NewTempRoot();
        try
        {
            var result = await service.AcquireAsync(Path.Combine(root, "data"), "op-ratio", TestIntent(zip), default);
            Assert.False(result.Succeeded);
            Assert.Equal(MinecraftArtifactErrorCodes.ArchiveUnsafe, result.SafeCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RecoveryTreatsPartialStagingAsAbsentAndUnexpectedTargetAsAmbiguous()
    {
        var zip = CreatePackZip();
        var service = new MinecraftArtifactAcquisitionService(new HttpClient(new StaticContentHandler(zip)));
        var intent = TestIntent(zip);
        var root = NewTempRoot();
        var target = Path.Combine(root, "data");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data.staging-op-recovery"));
            await File.WriteAllTextAsync(Path.Combine(root, "data.staging-op-recovery", "partial"), "partial");
            var partial = await service.InspectAsync(target, "op-recovery", intent, default);
            Assert.Equal(ProvisioningReconciliationOutcomes.EffectAbsent, partial.Outcome);

            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "foreign.txt"), "foreign");
            var conflict = await service.InspectAsync(target, "op-recovery", intent, default);
            Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous, conflict.Outcome);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FailedDownloadCanRetryFromCleanStaging()
    {
        var zip = CreatePackZip();
        var corrupt = zip.ToArray();
        corrupt[^1] ^= 0x01;
        var handler = new SequenceContentHandler(corrupt, zip);
        var service = new MinecraftArtifactAcquisitionService(new HttpClient(handler));
        var root = NewTempRoot();
        var target = Path.Combine(root, "data");
        try
        {
            var first = await service.AcquireAsync(target, "op-retry", TestIntent(zip), default);
            var retry = await service.AcquireAsync(target, "op-retry", TestIntent(zip), default);

            Assert.False(first.Succeeded);
            Assert.Equal(MinecraftArtifactErrorCodes.IdentityMismatch, first.SafeCode);
            Assert.True(retry.Succeeded, $"{retry.SafeCode}: {retry.SafeMessage}");
            Assert.Equal(2, handler.RequestCount);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] CreatePackZip(string? unsafeRelativePath = null, Action<ZipArchive>? customize = null)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            const string root = "pack";
            foreach (var directory in new[] { "config", "datapacks", "defaultconfigs", "kubejs", "modernfix", "mods", "schematics" })
                archive.CreateEntry($"{root}/{directory}/");
            WriteEntry(archive, $"{root}/server.properties", "server-port=25565\nonline-mode=true\n");
            WriteEntry(archive, $"{root}/mods/example.jar", "not-a-real-jar");
            WriteEntry(archive, $"{root}/start.sh", "must not be copied");
            WriteEntry(archive, $"{root}/old-mods-backup/old.jar", "must not be copied");
            if (unsafeRelativePath is not null)
                WriteEntry(archive, $"{root}/{unsafeRelativePath}", "unsafe");
            customize?.Invoke(archive);
        }
        return buffer.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
        writer.Write(value);
    }

    private static MinecraftInitialProvisioningConfiguration TestIntent(byte[] zip)
    {
        var approved = Assert.IsType<MinecraftInitialProvisioningConfiguration>(
            new MinecraftProvisioningConfigurationCodec().CreateInitial("Lunch", 6144, 25565, true).TypedValue);
        return approved with
        {
            ArtifactRoot = "pack",
            ArtifactSizeBytes = zip.LongLength,
            ArtifactSha256 = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant()
        };
    }

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gameshud-minecraft-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class StaticContentHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content)
            };
            response.Content.Headers.ContentLength = content.LongLength;
            return Task.FromResult(response);
        }
    }


    private sealed class SequenceContentHandler(params byte[][] contents) : HttpMessageHandler
    {
        private readonly Queue<byte[]> _contents = new(contents);
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = _contents.Dequeue();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content)
            };
            response.Content.Headers.ContentLength = content.LongLength;
            return Task.FromResult(response);
        }
    }
}
