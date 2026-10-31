using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

/// <summary>
/// JSON-per-instance store under
/// <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\instances\{Id}.json</c>. Each save is atomic
/// (write-temp + rename). Load is tolerant of stray / malformed files - they're skipped
/// silently rather than blowing up the launcher boot.
/// </summary>
public sealed class FileInstanceStore : IInstanceStore
{
    private readonly string _dir;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Use the platform-default directory.</summary>
    public FileInstanceStore() : this(DefaultDir()) { }

    /// <summary>Use an explicit directory (tests, advanced wiring).</summary>
    public FileInstanceStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _dir = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    /// <summary>The directory the store reads and writes from.</summary>
    public string Directory => _dir;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Instance>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!System.IO.Directory.Exists(_dir))
            return Array.Empty<Instance>();

        var list = new List<Instance>();
        foreach (var path in System.IO.Directory.EnumerateFiles(_dir, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var instance = await JsonSerializer.DeserializeAsync<Instance>(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (instance is not null) list.Add(instance);
            }
            catch
            {
                // Skip malformed files - don't block startup
            }
        }

        list.Sort(CompareNewestFirst);
        return list;
    }

    /// <inheritdoc />
    public async Task SaveAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(instance.Id);

        var path = Path.Combine(_dir, $"{instance.Id}.json");
        var tmp = path + ".tmp";

        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, instance, WriteOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tmp, path, overwrite: true);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var path = Path.Combine(_dir, $"{id}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private static int CompareNewestFirst(Instance a, Instance b)
    {
        // LastPlayedAt wins; CreatedAt is the tiebreaker; both descending.
        var aKey = a.LastPlayedAt ?? a.CreatedAt;
        var bKey = b.LastPlayedAt ?? b.CreatedAt;
        return bKey.CompareTo(aKey);
    }

    private static string DefaultDir()
    {
        // Instances are user-authored configuration: XDG_CONFIG_HOME on Linux, %LOCALAPPDATA% elsewhere.
        return Path.Combine(XdgPaths.AppFolder(DefaultEnvironment.Instance, XdgCategory.Config), "instances");
    }
}
