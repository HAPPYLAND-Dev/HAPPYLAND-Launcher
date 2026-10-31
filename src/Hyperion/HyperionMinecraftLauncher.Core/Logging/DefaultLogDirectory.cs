using System;
using System.IO;
using System.Runtime.InteropServices;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

/// <summary>
/// Resolves the per-platform default location for HyperionMinecraftLauncher's log files.
///
/// <list type="bullet">
///   <item>Windows: <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/logs</c></item>
///   <item>macOS:   <c>~/Library/Logs/HyperionMinecraftLauncher</c></item>
///   <item>Linux:   <c>$XDG_STATE_HOME/HyperionMinecraftLauncher</c> (XDG state spec is right for logs),
///                  else <c>~/.local/state/HyperionMinecraftLauncher</c>.</item>
/// </list>
/// </summary>
public static class DefaultLogDirectory
{
    /// <summary>The product-level folder name that the launcher claims under <c>LocalAppData</c>.</summary>
    public const string ApplicationName = "HyperionMinecraftLauncher";

    /// <summary>The leaf directory name (under <see cref="ApplicationName"/>) on Windows.</summary>
    public const string LogsSubdirectory = "logs";

    /// <summary>
    /// Returns the absolute path of the platform's preferred log directory.
    /// The directory itself is *not* created by this call - the file logger handles that lazily.
    /// </summary>
    /// <param name="env">
    /// Optional environment abstraction. Tests inject a fake; production code can pass <c>null</c>
    /// to use <see cref="DefaultEnvironment.Instance"/>.
    /// </param>
    public static string Resolve(IEnvironment? env = null)
    {
        env ??= DefaultEnvironment.Instance;

        if (env.CurrentPlatform == OSPlatform.Windows)
        {
            var local = env.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
                local = Path.Combine(Path.GetTempPath(), ApplicationName + "-data");
            return Path.Combine(local, ApplicationName, LogsSubdirectory);
        }

        if (env.CurrentPlatform == OSPlatform.OSX)
        {
            var home = env.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                home = Path.Combine(Path.GetTempPath(), ApplicationName + "-data");
            return Path.Combine(home, "Library", "Logs", ApplicationName);
        }

        // Linux + everything else: XDG state spec is the right home for logs.
        var stateHome = env.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrWhiteSpace(stateHome))
            return Path.Combine(stateHome, ApplicationName);

        var userHome = env.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userHome))
            return Path.Combine(userHome, ".local", "state", ApplicationName);

        // Last-ditch fallback: LocalApplicationData (mono maps this to ~/.local/share).
        var fallback = env.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(fallback))
            fallback = Path.Combine(Path.GetTempPath(), ApplicationName + "-data");
        return Path.Combine(fallback, ApplicationName, LogsSubdirectory);
    }
}
