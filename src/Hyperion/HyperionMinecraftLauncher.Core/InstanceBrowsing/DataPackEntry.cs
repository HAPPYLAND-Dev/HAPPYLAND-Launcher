using System;
using System.Collections.Generic;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// One zipped data pack under <c>&lt;gameDir&gt;/saves/&lt;world&gt;/datapacks/</c>. Unlike
/// resource and shader packs, data packs live per-world. The UI groups entries by
/// <see cref="WorldFolderName"/> so the user can tell which save a pack belongs to.
/// </summary>
public sealed record DataPackEntry
{
    /// <summary>Absolute path to the pack file on disk.</summary>
    public required string FullPath { get; init; }

    /// <summary>File name without directory (e.g. <c>"vanilla-tweaks.zip"</c>).</summary>
    public required string Filename { get; init; }

    /// <summary>File length in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Folder name of the world this pack belongs to (the parent's parent directory of the file).</summary>
    public required string WorldFolderName { get; init; }

    /// <summary>True when the file ends in <c>.zip</c>; false when the file ends in <c>.zip.disabled</c>.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Pack icon PNG (<c>pack.png</c>; for shader packs the first image found), or null.</summary>
    public byte[]? IconPng { get; init; }

    /// <summary>Formatted <c>pack.mcmeta</c> description, as the game's pack screen shows it.</summary>
    public IReadOnlyList<McTextSpan> Description { get; init; } = Array.Empty<McTextSpan>();

    /// <summary>Name as the game lists it: the file name without <c>.zip</c> / <c>.zip.disabled</c>.</summary>
    public string DisplayName => PackNames.Display(Filename);
}
