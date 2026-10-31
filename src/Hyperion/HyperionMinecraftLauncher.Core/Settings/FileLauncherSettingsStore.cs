using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;

/// <summary>JSON-on-disk settings store. Atomic save via write-temp + replace.</summary>
public sealed class FileLauncherSettingsStore : ILauncherSettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Use the platform-default path under <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/settings.json</c>.</summary>
    public FileLauncherSettingsStore()
        : this(DefaultPath())
    {
    }

    /// <summary>Use an explicit path. Useful for tests / advanced wiring.</summary>
    public FileLauncherSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>The path this store reads from / writes to.</summary>
    public string Path => _path;

    /// <inheritdoc />
    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return new LauncherSettings();

        try
        {
            await using var stream = File.OpenRead(_path);
            var loaded = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return loaded ?? new LauncherSettings();
        }
        catch
        {
            // Corrupt JSON - return defaults rather than crashing the launcher startup.
            return new LauncherSettings();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, WriteOptions, cancellationToken).ConfigureAwait(false);
        }

        // Atomic replace - on Windows, File.Move with overwrite is atomic on the same volume.
        File.Move(tmp, _path, overwrite: true);
    }

    private static string DefaultPath()
    {
        // Settings are configuration: XDG_CONFIG_HOME on Linux, %LOCALAPPDATA% elsewhere.
        var dir = XdgPaths.AppFolder(DefaultEnvironment.Instance, XdgCategory.Config);
        return System.IO.Path.Combine(dir, "settings.json");
    }
}
