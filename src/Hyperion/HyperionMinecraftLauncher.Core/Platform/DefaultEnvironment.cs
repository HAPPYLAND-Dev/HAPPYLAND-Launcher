using System;
using System.Runtime.InteropServices;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

/// <summary>
/// Production <see cref="IEnvironment"/> backed by <see cref="System.Environment"/> and
/// <see cref="RuntimeInformation"/>. Stateless; the static <see cref="Instance"/> is safe to share.
/// </summary>
public sealed class DefaultEnvironment : IEnvironment
{
    /// <summary>Shared singleton; safe because the type is stateless.</summary>
    public static DefaultEnvironment Instance { get; } = new();

    /// <inheritdoc />
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc />
    public string GetFolderPath(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);

    /// <inheritdoc />
    public OSPlatform CurrentPlatform
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return OSPlatform.Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return OSPlatform.OSX;
            return OSPlatform.Linux;
        }
    }
}
