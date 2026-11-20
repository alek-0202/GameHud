using System.Runtime.InteropServices;
using GamesHud.Api.HostCapabilities.Models;

namespace GamesHud.Api.HostCapabilities.Services;

public interface IHostSystemInspector
{
    HostSystemInspection GetSystemInfo();
}

public interface IHostSystemInfoProvider
{
    bool IsLinux { get; }

    bool IsWindows { get; }

    bool IsMacOS { get; }

    string OSDescription { get; }

    Architecture OSArchitecture { get; }

    Architecture ProcessArchitecture { get; }

    int LogicalProcessorCount { get; }
}

public sealed record HostSystemInspection(
    HostOperatingSystemInfo OperatingSystem,
    HostCpuInfo Cpu);

public sealed class HostSystemInspector : IHostSystemInspector
{
    private readonly IHostSystemInfoProvider _systemInfoProvider;

    public HostSystemInspector(IHostSystemInfoProvider systemInfoProvider)
    {
        _systemInfoProvider = systemInfoProvider;
    }

    public HostSystemInspection GetSystemInfo()
    {
        return new HostSystemInspection(
            new HostOperatingSystemInfo(
                ResolveOperatingSystemFamily(),
                _systemInfoProvider.OSDescription,
                FormatArchitecture(_systemInfoProvider.OSArchitecture)),
            new HostCpuInfo(
                Math.Max(1, _systemInfoProvider.LogicalProcessorCount),
                FormatArchitecture(_systemInfoProvider.ProcessArchitecture)));
    }

    private string ResolveOperatingSystemFamily()
    {
        if (_systemInfoProvider.IsLinux)
        {
            return "linux";
        }

        if (_systemInfoProvider.IsWindows)
        {
            return "windows";
        }

        if (_systemInfoProvider.IsMacOS)
        {
            return "macos";
        }

        return "unknown";
    }

    private static string FormatArchitecture(Architecture architecture)
    {
        return architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            Architecture.S390x => "s390x",
            Architecture.Wasm => "wasm",
            _ => architecture.ToString().ToLowerInvariant()
        };
    }
}

public sealed class RuntimeHostSystemInfoProvider : IHostSystemInfoProvider
{
    public bool IsLinux => OperatingSystem.IsLinux();

    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsMacOS => OperatingSystem.IsMacOS();

    public string OSDescription => RuntimeInformation.OSDescription;

    public Architecture OSArchitecture => RuntimeInformation.OSArchitecture;

    public Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;

    public int LogicalProcessorCount => Environment.ProcessorCount;
}
