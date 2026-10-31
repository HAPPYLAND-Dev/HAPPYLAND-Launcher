using System;
using System.Collections.Generic;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;

/// <summary>
/// One parsed Minecraft crash report (<c>crash-reports/crash-*-client.txt</c>).
/// Carries the raw text plus the structured fields the launcher UI surfaces:
/// header info, a one-line summary, and the ranked list of suspect mods.
/// </summary>
public sealed record CrashReport
{
    /// <summary>Absolute path to the source <c>.txt</c> file.</summary>
    public required string FilePath { get; init; }

    /// <summary>File name (no directory) - used for the list row label.</summary>
    public required string Filename { get; init; }

    /// <summary>UTC timestamp parsed from the filename (<c>crash-YYYY-MM-DD_HH.MM.SS-client.txt</c>); falls back to the file's mtime when the name does not parse.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Minecraft client version (e.g. <c>"1.20.1"</c>) from the <c>Minecraft Version:</c> header line. Empty when not present.</summary>
    public required string MinecraftVersion { get; init; }

    /// <summary>Loader version string (e.g. <c>"Forge 47.4.5"</c>, <c>"Fabric 0.16.5"</c>) when the report identifies one.</summary>
    public string? ForgeOrFabricVersion { get; init; }

    /// <summary>The line directly after <c>---- Minecraft Crash Report ----</c> (the joke quote on Forge, the description on others). Always one line.</summary>
    public required string SummaryLine { get; init; }

    /// <summary>Mods inferred to be the culprit, in confidence order (highest first). May be empty when no third-party package was on the stack.</summary>
    public required IReadOnlyList<CrashFrame> SuspectedMods { get; init; }

    /// <summary>The full unparsed report text - displayed verbatim in the viewer pane.</summary>
    public required string FullText { get; init; }
}
