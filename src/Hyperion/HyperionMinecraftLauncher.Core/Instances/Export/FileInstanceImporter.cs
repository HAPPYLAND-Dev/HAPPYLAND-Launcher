using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;

/// <summary>
/// Default <see cref="IInstanceImporter"/> implementation. Opens a Hyperion-format export zip,
/// validates the layout (must contain <c>hyperion-instance.json</c> at the root), extracts
/// <c>gameDir/*</c> into a fresh per-instance folder under
/// <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\instances-data\{NewId}\</c>, and returns the
/// rehydrated <see cref="Instance"/> record with a fresh GUID Id pointing at the extracted
/// folder.
/// </summary>
public sealed class FileInstanceImporter : IInstanceImporter
{
    private readonly string _importRoot;
    private readonly Func<string> _newId;

    /// <summary>Default ctor - extracts into LOCALAPPDATA / XDG_DATA_HOME under <c>instances-data/{NewId}</c>.</summary>
    public FileInstanceImporter() : this(DefaultRoot(), () => Guid.NewGuid().ToString("N")) { }

    /// <summary>Explicit ctor - lets tests pin both the root folder and the id generator.</summary>
    public FileInstanceImporter(string importRoot) : this(importRoot, () => Guid.NewGuid().ToString("N")) { }

    /// <summary>Explicit ctor with custom id generator (deterministic-id tests).</summary>
    public FileInstanceImporter(string importRoot, Func<string> newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importRoot);
        ArgumentNullException.ThrowIfNull(newId);
        _importRoot = importRoot;
        _newId = newId;
    }

    /// <inheritdoc />
    public async Task<Instance> ImportAsync(
        string sourceZipPath,
        string? overrideName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceZipPath);

        if (!File.Exists(sourceZipPath))
            throw new InstanceImportException($"Import file not found: {sourceZipPath}");

        Directory.CreateDirectory(_importRoot);

        ZipArchive archive;
        FileStream fs;
        try
        {
            fs = new FileStream(sourceZipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            archive = new ZipArchive(fs, ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new InstanceImportException("File is not a valid zip archive.", ex);
        }
        catch (IOException ex)
        {
            throw new InstanceImportException($"Could not open zip: {ex.Message}", ex);
        }

        try
        {
            var manifestEntry = archive.GetEntry("hyperion-instance.json");
            if (manifestEntry is null)
                throw new InstanceImportException(
                    "Invalid Hyperion instance zip: missing hyperion-instance.json at the archive root.");

            Instance? imported;
            try
            {
                await using var manifestStream = manifestEntry.Open();
                imported = await JsonSerializer.DeserializeAsync<Instance>(manifestStream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new InstanceImportException("Invalid Hyperion instance zip: hyperion-instance.json is not valid JSON.", ex);
            }

            if (imported is null)
                throw new InstanceImportException("Invalid Hyperion instance zip: hyperion-instance.json is empty.");

            // Generate a fresh Id + folder so the import doesn't collide with anything already on disk.
            var newId = _newId();
            var instanceFolder = Path.Combine(_importRoot, newId);
            Directory.CreateDirectory(instanceFolder);

            // Extract gameDir/* into the instance folder. Anything outside gameDir/ is ignored
            // (metadata.json + hyperion-instance.json are already consumed; everything else is
            // forward-compat surface).
            var totalGameDirBytes = 0L;
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith("gameDir/", StringComparison.Ordinal)) continue;
                if (entry.Length > 0) totalGameDirBytes += entry.Length;
            }
            if (totalGameDirBytes == 0) totalGameDirBytes = 1;

            long bytesSoFar = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                const string prefix = "gameDir/";
                if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;

                var relative = entry.FullName[prefix.Length..];
                if (string.IsNullOrEmpty(relative)) continue; // skip the folder entry itself

                // Hard-reject path traversal before writing anywhere.
                if (relative.Contains("..", StringComparison.Ordinal))
                    throw new InstanceImportException("Refusing to import zip with path traversal entries.");

                var target = Path.Combine(instanceFolder, relative.Replace('/', Path.DirectorySeparatorChar));
                var targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                if (relative.EndsWith('/'))
                    continue; // bare directory entry

                await using (var input = entry.Open())
                await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                bytesSoFar += entry.Length;
                progress?.Report(Math.Clamp((double)bytesSoFar / totalGameDirBytes, 0.0, 1.0));
            }

            progress?.Report(1.0);

            var finalName = string.IsNullOrWhiteSpace(overrideName) ? imported.Name : overrideName!;

            return imported with
            {
                Id = newId,
                Name = finalName,
                GameDirectory = instanceFolder,
                CreatedAt = DateTimeOffset.UtcNow,
                LastPlayedAt = null,
                IsAutoImported = false,
            };
        }
        finally
        {
            archive.Dispose();
            fs.Dispose();
        }
    }

    private static string DefaultRoot()
    {
        // Imported instance bodies live under the data root (not the config root) so they
        // don't get tangled up with the JSON-per-instance store under instances/.
        return Path.Combine(
            XdgPaths.AppFolder(DefaultEnvironment.Instance, XdgCategory.Data),
            "instances-data");
    }
}
