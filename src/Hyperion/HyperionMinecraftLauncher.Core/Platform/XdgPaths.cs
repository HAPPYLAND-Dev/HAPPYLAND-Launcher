using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

/// <summary>
/// XDG Base Directory aware path resolver shared by every per-platform store
/// in the Core library. The rules:
///
/// On Linux:
/// <list type="bullet">
///   <item>Config (settings, accounts, instances): <c>$XDG_CONFIG_HOME</c> else <c>~/.config</c></item>
///   <item>Data (skins history, etc.): <c>$XDG_DATA_HOME</c> else <c>~/.local/share</c></item>
///   <item>Cache: <c>$XDG_CACHE_HOME</c> else <c>~/.cache</c></item>
///   <item>State (logs): <c>$XDG_STATE_HOME</c> else <c>~/.local/state</c></item>
/// </list>
///
/// On Windows, every category collapses to <c>%LOCALAPPDATA%</c> - that's where the
/// product has always written and where Windows users expect to find it.
///
/// On macOS, every category collapses to <c>~/Library/Application Support</c>,
/// with the single exception of logs which live in <c>~/Library/Logs</c>.
/// </summary>
public static class XdgPaths
{
    /// <summary>The product folder name appended to whichever XDG / native root applies.</summary>
    public const string ApplicationName = "HyperionMinecraftLauncher";

    /// <summary>Per-user configuration directory. Linux: <c>$XDG_CONFIG_HOME</c>, else <c>~/.config</c>.</summary>
    public static string ConfigHome(IEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (env.CurrentPlatform == OSPlatform.Linux)
            return ResolveXdg(env, "XDG_CONFIG_HOME", relativeFallback: Path.Combine(".config"));
        return WindowsOrMacRoot(env);
    }

    /// <summary>Per-user data directory. Linux: <c>$XDG_DATA_HOME</c>, else <c>~/.local/share</c>.</summary>
    public static string DataHome(IEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (env.CurrentPlatform == OSPlatform.Linux)
            return ResolveXdg(env, "XDG_DATA_HOME", relativeFallback: Path.Combine(".local", "share"));
        return WindowsOrMacRoot(env);
    }

    /// <summary>Per-user cache directory. Linux: <c>$XDG_CACHE_HOME</c>, else <c>~/.cache</c>.</summary>
    public static string CacheHome(IEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (env.CurrentPlatform == OSPlatform.Linux)
            return ResolveXdg(env, "XDG_CACHE_HOME", relativeFallback: Path.Combine(".cache"));
        return WindowsOrMacRoot(env);
    }

    /// <summary>Per-user state directory (logs, history). Linux: <c>$XDG_STATE_HOME</c>, else <c>~/.local/state</c>.</summary>
    public static string StateHome(IEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (env.CurrentPlatform == OSPlatform.Linux)
            return ResolveXdg(env, "XDG_STATE_HOME", relativeFallback: Path.Combine(".local", "state"));
        return WindowsOrMacRoot(env);
    }

    /// <summary>
    /// Convenience: returns <c>{root}/HyperionMinecraftLauncher</c> for the named category.
    /// Use this from store classes - it keeps every "where do my files live?" call site short.
    /// </summary>
    public static string AppFolder(IEnvironment env, XdgCategory category)
    {
        var root = category switch
        {
            XdgCategory.Config => ConfigHome(env),
            XdgCategory.Data => DataHome(env),
            XdgCategory.Cache => CacheHome(env),
            XdgCategory.State => StateHome(env),
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
        if (string.IsNullOrEmpty(root))
            root = Path.Combine(Path.GetTempPath(), ApplicationName + "-data");
        return Path.Combine(root, ApplicationName);
    }

    private static string ResolveXdg(IEnvironment env, string varName, string relativeFallback)
    {
        var explicitDir = env.GetEnvironmentVariable(varName);
        if (!string.IsNullOrWhiteSpace(explicitDir))
            return explicitDir;

        var home = env.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            return string.Empty;
        return Path.Combine(home, relativeFallback);
    }

    private static string WindowsOrMacRoot(IEnvironment env)
    {
        // Windows: %LOCALAPPDATA%. macOS: ~/Library/Application Support.
        // Both are reachable through SpecialFolder.LocalApplicationData.
        return env.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }
}

/// <summary>Category of XDG directory - matches the four roles in the base-directory spec.</summary>
public enum XdgCategory
{
    /// <summary>Configuration (settings, accounts).</summary>
    Config,
    /// <summary>User data (skin history, downloaded resources).</summary>
    Data,
    /// <summary>Throw-away cache (news, profile pictures).</summary>
    Cache,
    /// <summary>Volatile state (logs, runtime history).</summary>
    State,
}
