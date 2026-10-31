using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Folder-per-server store under
/// <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/headless_servers/{Id}/</c>. Each folder holds:
/// <list type="bullet">
///   <item><c>metadata.json</c> - the <see cref="HeadlessServer"/> record.</item>
///   <item><c>eula.txt</c> - <c>eula=true</c>, written eagerly so a future "Start" wiring doesn't trip on it.</item>
///   <item><c>server.properties</c> - minimal config (just <c>server-port</c>).</item>
/// </list>
/// The actual <c>server.jar</c> download is deferred to v0.29 (TODO in <see cref="CreateAsync"/>).
/// </summary>
public sealed class FileHeadlessServerStore : IHeadlessServerStore
{
    private const string MetadataFileName = "metadata.json";
    private const string EulaFileName = "eula.txt";
    private const string PropertiesFileName = "server.properties";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _root;

    /// <summary>Use the platform-default directory.</summary>
    public FileHeadlessServerStore() : this(DefaultRoot()) { }

    /// <summary>Use an explicit root (tests, advanced wiring).</summary>
    public FileHeadlessServerStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
        Directory.CreateDirectory(root);
    }

    /// <summary>Absolute path of the storage root the store reads and writes from.</summary>
    public string Root => _root;

    /// <inheritdoc />
    public async Task<HeadlessServer> CreateAsync(HeadlessServerCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VersionId);

        var id = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);

        var server = new HeadlessServer
        {
            Id = id,
            Name = request.Name.Trim(),
            VersionId = request.VersionId,
            Path = dir,
            RamMb = request.RamMb,
            Port = request.Port,
            CreatedAt = DateTimeOffset.UtcNow,
            LastStartedAt = null,
        };

        // metadata.json
        var metadataPath = Path.Combine(dir, MetadataFileName);
        await using (var stream = File.Create(metadataPath))
        {
            await JsonSerializer.SerializeAsync(stream, server, WriteOptions, cancellationToken).ConfigureAwait(false);
        }

        // eula.txt - Mojang requires explicit acceptance before the dedicated server will boot.
        // We write it eagerly here under the assumption the user accepts the Mojang EULA by
        // creating the server through Hyperion's UI.
        await File.WriteAllTextAsync(Path.Combine(dir, EulaFileName), "eula=true" + Environment.NewLine, cancellationToken)
            .ConfigureAwait(false);

        // server.properties - bare minimum so the dedicated server doesn't fall back to its
        // own defaults the first time it starts. Everything else can be edited by the user
        // in the file directly.
        await File.WriteAllTextAsync(
            Path.Combine(dir, PropertiesFileName),
            $"server-port={request.Port}" + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);

        // TODO(v0.29): download the matching server jar (vanilla manifest -> downloads.server.url)
        // into {dir}/server.jar and surface a Start / Stop pipeline that spawns the JVM with -Xmx{RamMb}m.

        return server;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HeadlessServer>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root))
            return Array.Empty<HeadlessServer>();

        var list = new List<HeadlessServer>();
        foreach (var subdir in Directory.EnumerateDirectories(_root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(subdir, MetadataFileName);
            if (!File.Exists(metadataPath)) continue;
            try
            {
                await using var stream = File.OpenRead(metadataPath);
                var server = await JsonSerializer.DeserializeAsync<HeadlessServer>(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (server is not null) list.Add(server);
            }
            catch
            {
                // Skip malformed entries - one bad folder shouldn't hide the rest of the list.
            }
        }

        list.Sort(CompareNewestFirst);
        return list;
    }

    /// <inheritdoc />
    public async Task<HeadlessServer?> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var metadataPath = Path.Combine(_root, id, MetadataFileName);
        if (!File.Exists(metadataPath)) return null;
        try
        {
            await using var stream = File.OpenRead(metadataPath);
            return await JsonSerializer.DeserializeAsync<HeadlessServer>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var dir = Path.Combine(_root, id);
        if (Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort: ignore lingering file locks (e.g. the user has the folder open
                // in Explorer). Re-throwing here would surface a useless dialog in the UI.
            }
            catch (UnauthorizedAccessException)
            {
                // Same rationale - swallow rather than crash the page.
            }
        }
        return Task.CompletedTask;
    }

    private static int CompareNewestFirst(HeadlessServer a, HeadlessServer b)
        => b.CreatedAt.CompareTo(a.CreatedAt);

    private static string DefaultRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperionMinecraftLauncher", "headless_servers");
    }
}
