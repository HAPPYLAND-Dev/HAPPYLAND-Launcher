using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

/// <summary>
/// Default locator: returns the path Mojang's own launcher writes to.
/// <list type="bullet">
///   <item>Windows: <c>%APPDATA%\.minecraft</c></item>
///   <item>macOS: <c>~/Library/Application Support/minecraft</c> (note: no leading dot, no <c>.minecraft</c>)</item>
///   <item>Linux: <c>~/.minecraft</c> (XDG is intentionally ignored - the vanilla launcher hard-codes this path)</item>
/// </list>
/// </summary>
public sealed class DefaultMinecraftInstallationLocator : IMinecraftInstallationLocator
{
    /// <inheritdoc />
    public MinecraftInstallation Locate()
    {
        var root = ResolveRoot();
        return Build(root);
    }

    /// <summary>Build a <see cref="MinecraftInstallation"/> from an explicit root path. Useful for tests / advanced wiring.</summary>
    public static MinecraftInstallation Build(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return new MinecraftInstallation
        {
            Root = root,
            VersionsDirectory = Path.Combine(root, "versions"),
            LauncherProfilesPath = Path.Combine(root, "launcher_profiles.json"),
            LauncherAccountsPath = Path.Combine(root, "launcher_accounts.json"),
            ServersDatPath = Path.Combine(root, "servers.dat"),
        };
    }

    /// <summary>Compute the platform-default <c>.minecraft</c> root (without touching the disk).</summary>
    public static string ResolveRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft");
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "minecraft");
        }
        // Linux + everything else: $HOME/.minecraft.
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".minecraft");
    }
}
