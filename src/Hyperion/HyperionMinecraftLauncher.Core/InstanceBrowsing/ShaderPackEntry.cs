using System;
using System.Collections.Generic;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// One zipped shader pack under <c>&lt;gameDir&gt;/shaderpacks/</c>. Same shape as
/// <see cref="ResourcePackEntry"/>: Iris / OptiFine treat <c>.zip.disabled</c> as a skip
/// marker, so toggling enable/disable is a rename rather than a delete.
/// </summary>
public sealed record ShaderPackEntry
{
    /// <summary>Absolute path to the pack file on disk.</summary>
    public required string FullPath { get; init; }

    /// <summary>File name without directory (e.g. <c>"BSL_v8.zip"</c>).</summary>
    public required string Filename { get; init; }

    /// <summary>File length in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>True when the file ends in <c>.zip</c>; false when the file ends in <c>.zip.disabled</c>.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Pack icon PNG (<c>pack.png</c>; for shader packs the first image found), or null.</summary>
    public byte[]? IconPng { get; init; }

    /// <summary>Formatted <c>pack.mcmeta</c> description, as the game's pack screen shows it.</summary>
    public IReadOnlyList<McTextSpan> Description { get; init; } = Array.Empty<McTextSpan>();

    /// <summary>Name as the game lists it: the file name without <c>.zip</c> / <c>.zip.disabled</c>.</summary>
    public string DisplayName => PackNames.Display(Filename);
}
