using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.History;

/// <summary>
/// Disk-backed <see cref="ISkinHistoryStore"/>. PNGs live in
/// <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\skins_history\{Id}.png</c> and the index
/// (id + variant + timestamp tuples) is serialised as JSON in <c>index.json</c> in the
/// same directory. The store keeps the most recent <see cref="Capacity"/> entries and
/// rotates the oldest out on every <see cref="AppendAsync"/>.
/// </summary>
/// <remarks>
/// Writes are serialised through a single <see cref="SemaphoreSlim"/> so concurrent
/// callers (the UI never calls more than one at a time, but tests do) see a consistent
/// view. Both the file and its corresponding index slot are removed together; a malformed
/// index file is treated as empty.
/// </remarks>
public sealed class FileSkinHistoryStore : ISkinHistoryStore
{
    /// <summary>Hard cap on the number of entries kept on disk.</summary>
    public const int Capacity = 10;

    private const string IndexFileName = "index.json";

    private readonly string _baseDir;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Build a store rooted at the default platform path.</summary>
    public FileSkinHistoryStore() : this(DefaultBaseDir(), () => DateTimeOffset.UtcNow) { }

    public FileSkinHistoryStore(string baseDir) : this(baseDir, () => DateTimeOffset.UtcNow) { }

    public FileSkinHistoryStore(string baseDir, Func<DateTimeOffset> clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDir);
        ArgumentNullException.ThrowIfNull(clock);
        _baseDir = baseDir;
        _clock = clock;
        Directory.CreateDirectory(baseDir);
    }

    /// <summary>Absolute path to the directory the store reads from / writes to.</summary>
    public string BaseDirectory => _baseDir;

    /// <inheritdoc />
    public async Task AppendAsync(byte[] pngBytes, SkinVariant variant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0) throw new ArgumentException("Skin PNG bytes cannot be empty.", nameof(pngBytes));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = LoadIndex();

            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(_baseDir, id + ".png");
            await File.WriteAllBytesAsync(path, pngBytes, cancellationToken).ConfigureAwait(false);

            var entry = new SkinHistoryEntry
            {
                Id = id,
                FilePath = path,
                Variant = variant,
                SavedAt = _clock(),
            };

            // newest-first ordering; trim the tail so we never exceed Capacity.
            entries.Insert(0, entry);
            while (entries.Count > Capacity)
            {
                var evicted = entries[^1];
                entries.RemoveAt(entries.Count - 1);
                TryDelete(evicted.FilePath);
            }

            SaveIndex(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkinHistoryEntry>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return LoadIndex();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var e in LoadIndex())
                TryDelete(e.FilePath);

            var indexPath = Path.Combine(_baseDir, IndexFileName);
            TryDelete(indexPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Read the index file from disk, tolerating absence or corruption.</summary>
    private List<SkinHistoryEntry> LoadIndex()
    {
        var path = Path.Combine(_baseDir, IndexFileName);
        if (!File.Exists(path)) return new List<SkinHistoryEntry>();
        try
        {
            using var stream = File.OpenRead(path);
            var raw = JsonSerializer.Deserialize<List<PersistedEntry>>(stream, JsonOptions)
                      ?? new List<PersistedEntry>();
            var list = new List<SkinHistoryEntry>(raw.Count);
            foreach (var p in raw)
            {
                if (string.IsNullOrWhiteSpace(p.Id)) continue;
                var fp = p.FilePath;
                if (string.IsNullOrWhiteSpace(fp))
                    fp = Path.Combine(_baseDir, p.Id + ".png");
                list.Add(new SkinHistoryEntry
                {
                    Id = p.Id!,
                    FilePath = fp!,
                    Variant = p.Variant,
                    SavedAt = p.SavedAt,
                });
            }
            return list;
        }
        catch
        {
            // Corrupt index: treat as empty - the next AppendAsync will overwrite it.
            return new List<SkinHistoryEntry>();
        }
    }

    private void SaveIndex(IReadOnlyList<SkinHistoryEntry> entries)
    {
        var path = Path.Combine(_baseDir, IndexFileName);
        var tmp = path + ".tmp";
        var persisted = entries
            .Select(e => new PersistedEntry
            {
                Id = e.Id,
                FilePath = e.FilePath,
                Variant = e.Variant,
                SavedAt = e.SavedAt,
            })
            .ToList();

        var bytes = JsonSerializer.SerializeToUtf8Bytes(persisted, JsonOptions);
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best-effort
        }
    }

    private static string DefaultBaseDir()
    {
        // Skin history is user data: XDG_DATA_HOME (~/.local/share) on Linux, %LOCALAPPDATA% elsewhere.
        return Path.Combine(XdgPaths.AppFolder(DefaultEnvironment.Instance, XdgCategory.Data), "skins_history");
    }

    private sealed class PersistedEntry
    {
        public string? Id { get; set; }
        public string? FilePath { get; set; }
        public SkinVariant Variant { get; set; }
        public DateTimeOffset SavedAt { get; set; }
    }
}
