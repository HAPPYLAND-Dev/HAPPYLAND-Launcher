using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Playtime;

/// <summary>
/// JSON-backed <see cref="IPlaytimeStore"/> at <c>&lt;config&gt;/playtime.json</c>: total seconds per
/// instance id plus the journal of active sessions. Every operation is a read-modify-write
/// under one lock and the file is replaced atomically (write temp + rename).
/// </summary>
public sealed class FilePlaytimeStore : IPlaytimeStore
{
    /// <summary>Upper bound for a single credited session - guards against clock jumps and stuck journals.</summary>
    public static readonly TimeSpan MaxSessionLength = TimeSpan.FromHours(48);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Use the platform-default location next to the instance store.</summary>
    public FilePlaytimeStore()
        : this(Path.Combine(XdgPaths.AppFolder(DefaultEnvironment.Instance, XdgCategory.Config), "playtime.json")) { }

    /// <summary>Use an explicit file path (tests / advanced wiring).</summary>
    public FilePlaytimeStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <inheritdoc />
    public async Task<TimeSpan> GetTotalAsync(string instanceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var doc = await ReadLockedAsync(cancellationToken).ConfigureAwait(false);
        return doc.Totals.TryGetValue(instanceId, out var seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }

    /// <inheritdoc />
    public Task BeginSessionAsync(PlaySession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        return MutateAsync(doc =>
        {
            doc.Active.RemoveAll(s => s.ProcessId == session.ProcessId);
            doc.Active.Add(SessionDto.From(session));
            return TimeSpan.Zero;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TimeSpan> EndSessionAsync(int processId, DateTimeOffset endedAt, CancellationToken cancellationToken)
        => MutateAsync(doc =>
        {
            var dto = doc.Active.FirstOrDefault(s => s.ProcessId == processId);
            if (dto is null) return TimeSpan.Zero;
            doc.Active.Remove(dto);

            var length = endedAt - dto.StartedAt;
            if (length < TimeSpan.Zero) length = TimeSpan.Zero;
            if (length > MaxSessionLength) length = MaxSessionLength;

            doc.Totals[dto.InstanceId] = doc.Totals.GetValueOrDefault(dto.InstanceId) + (long)length.TotalSeconds;
            return TimeSpan.FromSeconds((long)length.TotalSeconds);
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaySession>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var doc = await ReadLockedAsync(cancellationToken).ConfigureAwait(false);
        return doc.Active.Select(s => s.ToSession()).ToList();
    }

    private async Task<Document> ReadLockedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<TimeSpan> MutateAsync(Func<Document, TimeSpan> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var doc = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var result = change(doc);
            await WriteAsync(doc, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Document> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new Document();
        try
        {
            await using var fs = File.OpenRead(_path);
            var doc = await JsonSerializer.DeserializeAsync<Document>(fs, JsonOptions, cancellationToken).ConfigureAwait(false);
            return doc ?? new Document();
        }
        catch (JsonException)
        {
            // A damaged file must not block launching; start over rather than crash.
            return new Document();
        }
    }

    private async Task WriteAsync(Document doc, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, doc, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        File.Move(tmp, _path, overwrite: true);
    }

    private sealed class Document
    {
        [JsonPropertyName("totals")]
        public Dictionary<string, long> Totals { get; set; } = new(StringComparer.Ordinal);

        [JsonPropertyName("active")]
        public List<SessionDto> Active { get; set; } = new();
    }

    private sealed class SessionDto
    {
        [JsonPropertyName("instanceId")] public string InstanceId { get; set; } = string.Empty;
        [JsonPropertyName("processId")] public int ProcessId { get; set; }
        [JsonPropertyName("startedAt")] public DateTimeOffset StartedAt { get; set; }
        [JsonPropertyName("gameDirectory")] public string? GameDirectory { get; set; }

        public static SessionDto From(PlaySession s) => new()
        {
            InstanceId = s.InstanceId,
            ProcessId = s.ProcessId,
            StartedAt = s.StartedAt,
            GameDirectory = s.GameDirectory,
        };

        public PlaySession ToSession() => new()
        {
            InstanceId = InstanceId,
            ProcessId = ProcessId,
            StartedAt = StartedAt,
            GameDirectory = GameDirectory,
        };
    }
}
