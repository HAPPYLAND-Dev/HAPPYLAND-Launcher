using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;

/// <summary>
/// Default <see cref="IInstanceExporter"/> implementation: copies the relevant slice of an
/// instance's game directory into a fresh <c>.zip</c> file, alongside a serialised copy of
/// the <see cref="Instance"/> record (<c>hyperion-instance.json</c>) and a small
/// <c>metadata.json</c> sidecar.
///
/// Folder filter:
/// <list type="bullet">
///   <item>Always included: <c>mods/</c>, <c>config/</c>, <c>resourcepacks/</c>, <c>shaderpacks/</c>, and any flat config files at the gameDir root (e.g. <c>options.txt</c>).</item>
///   <item>Always excluded: <c>logs/</c>, <c>crash-reports/</c>, <c>versions/</c>, <c>libraries/</c>, <c>assets/</c> (Mojang launcher caches), <c>.fabric/</c>, <c>.cache/</c>, runtime exe folders.</item>
///   <item>Opt-in via <see cref="ExportOptions"/>: <c>saves/</c>, <c>screenshots/</c>.</item>
/// </list>
///
/// Uses <see cref="ZipFile.Open(string,ZipArchiveMode)"/> in <see cref="ZipArchiveMode.Create"/>
/// so the destination zip is always a fresh stream; pre-existing files are replaced.
/// </summary>
public sealed class FileInstanceExporter : IInstanceExporter
{
    private readonly string _exporterVersion;
    private readonly Func<DateTimeOffset> _utcNow;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Default ctor - exporter stamps the running assembly version into <c>metadata.json</c>.</summary>
    public FileInstanceExporter() : this(ResolveAssemblyVersion(), () => DateTimeOffset.UtcNow) { }

    /// <summary>Explicit ctor - used by tests to pin version/timestamp and produce deterministic output.</summary>
    public FileInstanceExporter(string exporterVersion, Func<DateTimeOffset> utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exporterVersion);
        ArgumentNullException.ThrowIfNull(utcNow);
        _exporterVersion = exporterVersion;
        _utcNow = utcNow;
    }

    /// <inheritdoc />
    public Task ExportAsync(Instance instance, string destinationZipPath, IProgress<double>? progress, CancellationToken cancellationToken)
        => ExportAsync(instance, destinationZipPath, ExportOptions.Default, progress, cancellationToken);

    /// <inheritdoc />
    public async Task ExportAsync(
        Instance instance,
        string destinationZipPath,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);
        ArgumentNullException.ThrowIfNull(options);

        // ZipFile.Open(Create) refuses to overwrite an existing file; do the cleanup ourselves
        // so callers don't have to remember to delete the target first.
        if (File.Exists(destinationZipPath))
            File.Delete(destinationZipPath);

        var parent = Path.GetDirectoryName(destinationZipPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        var gameDir = instance.GameDirectory;
        var fileList = new List<(string AbsolutePath, string EntryName)>();
        if (!string.IsNullOrWhiteSpace(gameDir) && Directory.Exists(gameDir))
        {
            fileList.AddRange(EnumerateGameDirFiles(gameDir!, options));
        }

        long totalBytes = 0;
        foreach (var (abs, _) in fileList)
        {
            try { totalBytes += new FileInfo(abs).Length; } catch { /* ignore unreadable */ }
        }
        if (totalBytes == 0) totalBytes = 1; // avoid divide-by-zero for "empty gameDir" exports

        await Task.Run(async () =>
        {
            using var fs = new FileStream(destinationZipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

            // 1) hyperion-instance.json (the source of truth on re-import).
            await WriteJsonEntryAsync(archive, "hyperion-instance.json", instance, cancellationToken).ConfigureAwait(false);

            // 2) metadata.json (FormatVersion + producer info).
            var meta = new InstanceExportMetadata(
                InstanceExportMetadata.CurrentFormatVersion,
                _exporterVersion,
                _utcNow());
            await WriteJsonEntryAsync(archive, "metadata.json", meta, cancellationToken).ConfigureAwait(false);

            // 3) gameDir/* (filtered).
            long bytesSoFar = 0;
            foreach (var (abs, entryName) in fileList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var zipEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                await using (var input = new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var output = zipEntry.Open())
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    bytesSoFar += input.Length;
                }

                progress?.Report(Math.Clamp((double)bytesSoFar / totalBytes, 0.0, 1.0));
            }

            // Always close on a 1.0 tick so progress UI lands at the right end-state.
            progress?.Report(1.0);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteJsonEntryAsync<T>(ZipArchive archive, string entryName, T payload, CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, payload, WriteOptions, ct).ConfigureAwait(false);
    }

    private static IEnumerable<(string AbsolutePath, string EntryName)> EnumerateGameDirFiles(string gameDir, ExportOptions options)
    {
        // Folder allow-list. Saves + screenshots are gated behind ExportOptions.
        var alwaysIn = new[] { "mods", "config", "resourcepacks", "shaderpacks", "schematics", "datapacks" };

        foreach (var folder in alwaysIn)
        {
            var src = Path.Combine(gameDir, folder);
            if (Directory.Exists(src))
            {
                foreach (var pair in EnumerateRecursive(src, $"gameDir/{folder}"))
                    yield return pair;
            }
        }

        if (options.IncludeSaves || options.IncludeWorldData)
        {
            var src = Path.Combine(gameDir, "saves");
            if (Directory.Exists(src))
            {
                foreach (var pair in EnumerateRecursive(src, "gameDir/saves"))
                    yield return pair;
            }
        }

        if (options.IncludeScreenshots)
        {
            var src = Path.Combine(gameDir, "screenshots");
            if (Directory.Exists(src))
            {
                foreach (var pair in EnumerateRecursive(src, "gameDir/screenshots"))
                    yield return pair;
            }
        }

        // Flat config files at the gameDir root (options.txt, servers.dat, optionsof.txt, ...).
        // Skip anything that lives inside one of the explicitly-excluded subdirs - that's already filtered above.
        foreach (var file in Directory.EnumerateFiles(gameDir))
        {
            var name = Path.GetFileName(file);
            if (IsRootFileExportable(name))
                yield return (file, $"gameDir/{name}");
        }
    }

    private static IEnumerable<(string AbsolutePath, string EntryName)> EnumerateRecursive(string root, string entryPrefix)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            yield return (file, $"{entryPrefix}/{rel}");
        }
    }

    private static bool IsRootFileExportable(string name)
    {
        // Skip Mojang launcher artefacts that we don't own.
        var skip = new[] { "launcher_profiles.json", "launcher_accounts.json", "usercache.json" };
        return !skip.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveAssemblyVersion()
    {
        var asm = typeof(FileInstanceExporter).Assembly;
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (!string.IsNullOrWhiteSpace(infoVer?.InformationalVersion))
        {
            // Strip the metadata after '+' (git hash, etc.) if present.
            var v = infoVer!.InformationalVersion;
            var plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
