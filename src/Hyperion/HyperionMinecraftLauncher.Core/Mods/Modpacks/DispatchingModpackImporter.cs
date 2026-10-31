using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

/// <summary>
/// Format-agnostic facade: wraps a Modrinth importer and a CurseForge importer and
/// dispatches to whichever one matches the archive contents. App-layer DI binds
/// <see cref="IModpackImporter"/> to this so the view-model can just call
/// <see cref="ImportAsync"/> without caring which file format the user picked.
/// </summary>
public sealed class DispatchingModpackImporter : IModpackImporter
{
    private readonly IModpackImporter _modrinth;
    private readonly IModpackImporter _curseForge;

    /// <summary>Construct with the per-format importers. Both must be non-null.</summary>
    public DispatchingModpackImporter(IModpackImporter modrinth, IModpackImporter curseForge)
    {
        _modrinth = modrinth ?? throw new ArgumentNullException(nameof(modrinth));
        _curseForge = curseForge ?? throw new ArgumentNullException(nameof(curseForge));
    }

    /// <summary>
    /// Read only the manifest of a modpack archive (no downloads) so the UI can show what is
    /// about to be imported. Throws <see cref="NotSupportedException"/> for archives that are
    /// neither a <c>.mrpack</c> nor a CurseForge pack, and <see cref="InvalidDataException"/>
    /// for a malformed or unsafe manifest.
    /// </summary>
    public static ModpackManifest ReadManifest(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        using var fs = File.OpenRead(archivePath);
        var format = ModpackFormatDetector.Detect(fs);
        fs.Position = 0;
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        return format switch
        {
            ModpackFormat.Modrinth => ModrinthModpackImporter.ParseManifest(zip),
            ModpackFormat.CurseForge => CurseForgeModpackImporter.ParseManifest(zip).Manifest,
            _ => throw new NotSupportedException(
                $"Archive '{Path.GetFileName(archivePath)}' is not a recognised modpack."),
        };
    }

    /// <inheritdoc />
    public Task<Instance> ImportAsync(
        string archivePath,
        string? targetInstanceName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var format = ModpackFormatDetector.Detect(archivePath);
        return format switch
        {
            ModpackFormat.Modrinth => _modrinth.ImportAsync(archivePath, targetInstanceName, progress, cancellationToken),
            ModpackFormat.CurseForge => _curseForge.ImportAsync(archivePath, targetInstanceName, progress, cancellationToken),
            _ => throw new NotSupportedException(
                $"Archive '{System.IO.Path.GetFileName(archivePath)}' is not a recognised modpack " +
                "(needs a top-level 'modrinth.index.json' for .mrpack or 'manifest.json' for CurseForge)."),
        };
    }
}
