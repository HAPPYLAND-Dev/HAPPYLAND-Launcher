using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;

/// <summary>
/// Default <see cref="IMultiMcInstanceImporter"/>. Pulls a MultiMC / Prism Launcher instance
/// apart - either from the user-picked <c>.zip</c> (extracted to a temp dir first) or directly
/// from a copy of the on-disk instance folder - parses <c>instance.cfg</c> and <c>mmc-pack.json</c>,
/// then copies the embedded <c>.minecraft/</c> tree (minus the official launcher's
/// <c>versions/</c> subfolder, which Hyperion manages globally) into a fresh per-instance
/// directory under Hyperion's data root.
/// </summary>
public sealed class MultiMcInstanceImporter : IMultiMcInstanceImporter
{
    private readonly string _importRoot;
    private readonly Func<string> _newId;

    /// <summary>Default ctor - extracts into LOCALAPPDATA / XDG_DATA_HOME under <c>instances-data/{NewId}</c>.</summary>
    public MultiMcInstanceImporter() : this(DefaultRoot(), () => Guid.NewGuid().ToString("N")) { }

    /// <summary>Explicit ctor - lets tests pin the root folder.</summary>
    public MultiMcInstanceImporter(string importRoot) : this(importRoot, () => Guid.NewGuid().ToString("N")) { }

    /// <summary>Explicit ctor with custom id generator (deterministic-id tests).</summary>
    public MultiMcInstanceImporter(string importRoot, Func<string> newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importRoot);
        ArgumentNullException.ThrowIfNull(newId);
        _importRoot = importRoot;
        _newId = newId;
    }

    /// <inheritdoc />
    public async Task<Instance> ImportAsync(
        string sourceZipOrFolderPath,
        string? overrideName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceZipOrFolderPath);

        // Resolve the source to an on-disk folder. If the user picked a zip we extract it
        // into a temp dir first; otherwise we use the folder verbatim. The split lets the
        // rest of the importer work on plain files regardless of the input shape.
        var sourceFolder = await MaterialiseSourceAsync(sourceZipOrFolderPath, cancellationToken).ConfigureAwait(false);

        try
        {
            var cfgPath = Path.Combine(sourceFolder, "instance.cfg");
            var packPath = Path.Combine(sourceFolder, "mmc-pack.json");

            if (!File.Exists(cfgPath))
                throw new InstanceImportException(
                    "Invalid MultiMC / Prism instance: missing instance.cfg at the root.");
            if (!File.Exists(packPath))
                throw new InstanceImportException(
                    "Invalid MultiMC / Prism instance: missing mmc-pack.json at the root.");

            MultiMcInstanceCfg cfg;
            try
            {
                var rawCfg = await File.ReadAllTextAsync(cfgPath, cancellationToken).ConfigureAwait(false);
                cfg = MultiMcInstanceCfgParser.Parse(rawCfg);
            }
            catch (IOException ex)
            {
                throw new InstanceImportException($"Could not read instance.cfg: {ex.Message}", ex);
            }

            MultiMcPack pack;
            try
            {
                var rawPack = await File.ReadAllTextAsync(packPath, cancellationToken).ConfigureAwait(false);
                pack = MultiMcPackParser.Parse(rawPack);
            }
            catch (InvalidDataException ex)
            {
                throw new InstanceImportException($"Could not parse mmc-pack.json: {ex.Message}", ex);
            }
            catch (IOException ex)
            {
                throw new InstanceImportException($"Could not read mmc-pack.json: {ex.Message}", ex);
            }

            // Allocate the destination folder up front - if the caller's id generator returns
            // an already-existing id we throw rather than silently merge into another instance.
            var newId = _newId();
            var instanceFolder = Path.Combine(_importRoot, newId);
            try
            {
                Directory.CreateDirectory(_importRoot);
                Directory.CreateDirectory(instanceFolder);
            }
            catch (IOException ex)
            {
                throw new InstanceImportException($"Could not create instance folder: {ex.Message}", ex);
            }

            // Copy .minecraft/* into the new folder. We deliberately drop versions/ - Hyperion
            // stores Mojang/loader versions globally under .minecraft/versions/ and re-creating
            // a per-instance copy would just waste disk + risk drift from the canonical install.
            var dotMinecraft = Path.Combine(sourceFolder, ".minecraft");
            // Prism's "Don't load extra dirs" / "minecraft" folder name fallbacks: very old
            // MultiMC builds used "minecraft" (no dot). Accept both so old shares still import.
            if (!Directory.Exists(dotMinecraft))
            {
                var fallback = Path.Combine(sourceFolder, "minecraft");
                if (Directory.Exists(fallback)) dotMinecraft = fallback;
            }

            if (Directory.Exists(dotMinecraft))
            {
                try
                {
                    await CopyTreeAsync(dotMinecraft, instanceFolder, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    throw new InstanceImportException($"Could not copy game directory: {ex.Message}", ex);
                }
            }
            else
            {
                // No game dir in the share - that's allowed (mod-light shares ship only the
                // configs); the user just gets an empty per-instance folder.
                progress?.Report(1.0);
            }

            // Map iconKey to a Hyperion icon; fallback to grass_block_side when unknown / null.
            var iconKey = MapIconKey(cfg.IconKey);

            var finalName = !string.IsNullOrWhiteSpace(overrideName)
                ? overrideName!
                : (!string.IsNullOrWhiteSpace(cfg.Name) ? cfg.Name! : "Imported from MultiMC");

            return new Instance
            {
                Id = newId,
                Name = finalName,
                VersionId = pack.MinecraftVersion,
                Loader = pack.Loader,
                LoaderVersion = pack.LoaderVersion,
                IconKey = iconKey,
                JvmArguments = cfg.JvmArgs,
                MinimumRamMb = cfg.MinMemAllocMb,
                MaximumRamMb = cfg.MaxMemAllocMb,
                GameDirectory = instanceFolder,
                CreatedAt = DateTimeOffset.UtcNow,
                LastPlayedAt = null,
                IsAutoImported = false,
            };
        }
        finally
        {
            // If we extracted into a temp dir, clean it up. Real folders (the user's own
            // unzipped instance) are never touched.
            if (_tempExtractedPath is { } temp && Directory.Exists(temp))
            {
                try { Directory.Delete(temp, recursive: true); }
                catch { /* best-effort */ }
                _tempExtractedPath = null;
            }
        }
    }

    private string? _tempExtractedPath;

    private async Task<string> MaterialiseSourceAsync(string path, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path))
        {
            // Folder import - look for instance.cfg either directly or in a single
            // subdirectory (Prism's zip extracts to a folder named after the instance, so
            // when a user manually unzips first they often end up one level deep).
            if (File.Exists(Path.Combine(path, "instance.cfg")))
                return path;

            var subdirs = Directory.GetDirectories(path);
            if (subdirs.Length == 1 && File.Exists(Path.Combine(subdirs[0], "instance.cfg")))
                return subdirs[0];

            return path; // let the importer raise the "missing instance.cfg" error
        }

        if (!File.Exists(path))
            throw new InstanceImportException($"Source path not found: {path}");

        var tempBase = Path.Combine(Path.GetTempPath(), "hyperion-mmc-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempBase);
        _tempExtractedPath = tempBase;

        try
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.FullName.Contains("..", StringComparison.Ordinal))
                    throw new InstanceImportException("Refusing to import zip with path traversal entries.");

                var target = Path.Combine(tempBase, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                var targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

                await using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new InstanceImportException("File is not a valid zip archive.", ex);
        }
        catch (IOException ex)
        {
            throw new InstanceImportException($"Could not extract zip: {ex.Message}", ex);
        }

        // Prism's exports nest the whole instance under a single named root inside the zip
        // (e.g. "MyPack/instance.cfg") rather than splatting at root. Detect both shapes.
        if (File.Exists(Path.Combine(tempBase, "instance.cfg")))
            return tempBase;

        var children = Directory.GetDirectories(tempBase);
        if (children.Length == 1 && File.Exists(Path.Combine(children[0], "instance.cfg")))
            return children[0];

        return tempBase; // let the parent raise the "missing instance.cfg" error
    }

    private static async Task CopyTreeAsync(
        string sourceDir,
        string destDir,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        // Build the list of files first so we have a denominator for progress and so we can
        // skip versions/ uniformly regardless of directory traversal order.
        var files = new List<string>();
        foreach (var f in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            // The official launcher's versions/ folder contains per-version manifests Hyperion
            // already owns; copying them in just makes a confusing duplicate.
            var rel = Path.GetRelativePath(sourceDir, f).Replace('\\', '/');
            if (rel.StartsWith("versions/", StringComparison.OrdinalIgnoreCase)) continue;
            files.Add(f);
        }

        if (files.Count == 0)
        {
            progress?.Report(1.0);
            return;
        }

        long totalBytes = 0;
        foreach (var f in files)
        {
            try { totalBytes += new FileInfo(f).Length; }
            catch { /* unreadable file - count as zero, treated as no-op below */ }
        }
        if (totalBytes <= 0) totalBytes = 1;

        long sent = 0;
        foreach (var src in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rel = Path.GetRelativePath(sourceDir, src);
            var dest = Path.Combine(destDir, rel);
            var destSubdir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destSubdir)) Directory.CreateDirectory(destSubdir);

            await using var inStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var outStream = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
            await inStream.CopyToAsync(outStream, cancellationToken).ConfigureAwait(false);

            try { sent += new FileInfo(src).Length; }
            catch { /* ignored */ }
            progress?.Report(Math.Clamp((double)sent / totalBytes, 0.0, 1.0));
        }

        progress?.Report(1.0);
    }

    /// <summary>
    /// Translate a MultiMC / Prism icon key to one of Hyperion's curated icon keys. The Prism
    /// icon set is much richer than ours, so this is a best-effort mapping with a
    /// <see cref="InstanceIcons.GrassBlock"/> fallback rather than a strict 1:1 table.
    /// </summary>
    public static string MapIconKey(string? mmcIconKey)
    {
        if (string.IsNullOrWhiteSpace(mmcIconKey)) return InstanceIcons.GrassBlock;

        return mmcIconKey switch
        {
            "grass" or "grass_block" => InstanceIcons.GrassBlock,
            "dirt" => InstanceIcons.Dirt,
            "stone" => InstanceIcons.Stone,
            "cobblestone" => InstanceIcons.Cobblestone,
            "planks" or "oak_planks" => InstanceIcons.Planks,
            "chest" or "ender_chest" => InstanceIcons.Chest,
            "compass" or "modrinth" => InstanceIcons.Compass,
            "clock" => InstanceIcons.Clock,
            "redstone" or "redstone_dust" => InstanceIcons.Redstone,
            "diamond_pickaxe" or "diamond" => InstanceIcons.DiamondPickaxe,
            "iron_pickaxe" or "iron" => InstanceIcons.IronPickaxe,
            "book" or "enchanting_table" => InstanceIcons.Book,
            "writable_book" => InstanceIcons.WritableBook,
            "comparator" => InstanceIcons.Comparator,
            "ender_pearl" => InstanceIcons.EnderPearl,
            // Forge / Fabric / NeoForge / Quilt / flame icons are common in Prism shares;
            // we don't have direct equivalents - default to the diamond pickaxe so the
            // imported instance still feels distinctive vs a fresh vanilla tile.
            "flame" or "forge" or "fabric" or "quilt" or "neoforged" => InstanceIcons.DiamondPickaxe,
            _ => InstanceIcons.GrassBlock,
        };
    }

    private static string DefaultRoot()
    {
        // Same data root as FileInstanceImporter - imported game dirs live under instances-data/
        // so the instances/ JSON store stays a flat metadata catalog.
        return Path.Combine(
            XdgPaths.AppFolder(DefaultEnvironment.Instance, XdgCategory.Data),
            "instances-data");
    }
}
