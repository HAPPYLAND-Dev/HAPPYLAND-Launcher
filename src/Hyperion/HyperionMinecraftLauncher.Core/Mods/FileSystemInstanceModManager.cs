using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;

/// <summary>
/// Disk-backed <see cref="IInstanceModManager"/>: writes mods to
/// <c>&lt;Instance.GameDirectory ?? OS-default&gt;/mods/</c>. Disabled mods are kept on disk
/// with a <c>.jar.disabled</c> suffix - the same convention used by the official Minecraft
/// launcher, MultiMC, Prism, etc. - so a toggle is a rename, never a re-download.
/// </summary>
public sealed class FileSystemInstanceModManager : IInstanceModManager
{
    private const string DisabledSuffix = ".disabled";

    /// <inheritdoc />
    public Task<IReadOnlyList<LocalMod>> ListInstalledAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var modsDir = ResolveModsDirectory(instance);
        if (!Directory.Exists(modsDir))
            return Task.FromResult<IReadOnlyList<LocalMod>>(Array.Empty<LocalMod>());

        return Task.Run<IReadOnlyList<LocalMod>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = new List<LocalMod>();
            foreach (var path in Directory.EnumerateFiles(modsDir))
            {
                var name = Path.GetFileName(path);
                var lower = name.ToLowerInvariant();
                var enabled = lower.EndsWith(".jar", StringComparison.Ordinal);
                var disabled = lower.EndsWith(".jar" + DisabledSuffix, StringComparison.Ordinal);
                if (!enabled && !disabled) continue;

                long size = 0;
                try { size = new FileInfo(path).Length; } catch { /* file gone mid-scan */ }

                list.Add(new LocalMod
                {
                    Filename = name,
                    DisplayName = StripExtensions(name),
                    SizeBytes = size,
                    IsEnabled = enabled,
                });
            }
            return list;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task InstallAsync(Instance instance, ModFile file, IModRepository repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(repository);

        var modsDir = ResolveModsDirectory(instance);
        Directory.CreateDirectory(modsDir);
        var filename = string.IsNullOrEmpty(file.Filename)
            ? DeriveFilenameFromUrl(file.DownloadUrl)
            : file.Filename;
        var tmp = Path.Combine(modsDir, filename + ".part");
        var final = Path.Combine(modsDir, filename);

        await using (var fs = File.Create(tmp))
        {
            await repository.DownloadAsync(file, fs, progress: null, cancellationToken).ConfigureAwait(false);
        }

        // Atomic-ish replace; on the same volume File.Move with overwrite swaps in one step.
        if (File.Exists(final)) File.Delete(final);
        File.Move(tmp, final);
    }

    /// <inheritdoc />
    public Task RemoveAsync(Instance instance, string filename, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        var modsDir = ResolveModsDirectory(instance);
        var target = Path.Combine(modsDir, filename);
        if (File.Exists(target))
            File.Delete(target);

        // Also try the toggled name so callers can pass either form.
        if (filename.EndsWith(DisabledSuffix, StringComparison.Ordinal))
        {
            var withoutDisabled = filename.Substring(0, filename.Length - DisabledSuffix.Length);
            var alt = Path.Combine(modsDir, withoutDisabled);
            if (File.Exists(alt)) File.Delete(alt);
        }
        else
        {
            var alt = Path.Combine(modsDir, filename + DisabledSuffix);
            if (File.Exists(alt)) File.Delete(alt);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetEnabledAsync(Instance instance, string filename, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        var modsDir = ResolveModsDirectory(instance);
        var isCurrentlyDisabled = filename.EndsWith(DisabledSuffix, StringComparison.Ordinal);
        var source = Path.Combine(modsDir, filename);

        if (enabled && isCurrentlyDisabled)
        {
            var dest = Path.Combine(modsDir, filename.Substring(0, filename.Length - DisabledSuffix.Length));
            if (File.Exists(source))
            {
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(source, dest);
            }
        }
        else if (!enabled && !isCurrentlyDisabled)
        {
            var dest = Path.Combine(modsDir, filename + DisabledSuffix);
            if (File.Exists(source))
            {
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(source, dest);
            }
        }
        // else: already in requested state - no-op.

        return Task.CompletedTask;
    }

    private static string ResolveModsDirectory(Instance instance)
    {
        var root = string.IsNullOrWhiteSpace(instance.GameDirectory)
            ? DefaultMinecraftInstallationLocator.ResolveRoot()
            : instance.GameDirectory;
        return Path.Combine(root, "mods");
    }

    private static string StripExtensions(string filename)
    {
        var s = filename;
        if (s.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
            s = s.Substring(0, s.Length - DisabledSuffix.Length);
        if (s.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(0, s.Length - ".jar".Length);
        return s;
    }

    private static string DeriveFilenameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var last = uri.Segments.Length > 0 ? Uri.UnescapeDataString(uri.Segments[^1]) : null;
            if (!string.IsNullOrWhiteSpace(last)) return last;
        }
        catch { }
        return "mod.jar";
    }
}
