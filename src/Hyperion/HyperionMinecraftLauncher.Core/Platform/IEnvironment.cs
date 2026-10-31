using System;
using System.Runtime.InteropServices;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

/// <summary>
/// Thin abstraction over the parts of <see cref="System.Environment"/> and
/// <see cref="RuntimeInformation"/> that platform-aware path resolvers need.
///
/// The only reason this exists is testability: production code uses
/// <see cref="DefaultEnvironment"/>, tests inject a fake to control
/// <c>$XDG_STATE_HOME</c>, <c>$HOME</c>, and the reported OS.
/// </summary>
public interface IEnvironment
{
    /// <summary>Reads an environment variable, returning <c>null</c> when unset or blank.</summary>
    string? GetEnvironmentVariable(string name);

    /// <summary>Returns the absolute path of a well-known folder (e.g. user profile, local-app-data).</summary>
    string GetFolderPath(Environment.SpecialFolder folder);

    /// <summary>The current OS platform - one of <see cref="OSPlatform.Windows"/>, <see cref="OSPlatform.Linux"/>, <see cref="OSPlatform.OSX"/>.</summary>
    OSPlatform CurrentPlatform { get; }
}
