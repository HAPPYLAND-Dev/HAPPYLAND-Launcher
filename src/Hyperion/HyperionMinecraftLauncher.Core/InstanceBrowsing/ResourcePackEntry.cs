using System;
using System.Collections.Generic;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// One zipped resource pack under <c>&lt;gameDir&gt;/resourcepacks/</c>. Vanilla Minecraft
/// honours the <c>.zip.disabled</c> convention: rename a pack to that suffix and the game
/// will skip loading it. The launcher uses the same convention so toggling a pack on/off
/// is a single <c>File.Move</c> away.
/// </summary>
public sealed record ResourcePackEntry
{
    /// <summary>Absolute path to the pack file on disk.</summary>
    public required string FullPath { get; init; }

    /// <summary>File name without directory (e.g. <c>"FaithfulPBR.zip"</c> or <c>"BareBones.zip.disabled"</c>).</summary>
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
