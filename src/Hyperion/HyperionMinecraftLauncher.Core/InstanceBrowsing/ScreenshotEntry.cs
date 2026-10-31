using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// One PNG under <c>&lt;gameDir&gt;/screenshots/</c>. <see cref="Width"/> and <see cref="Height"/>
/// come from parsing the 13-byte IHDR header (8-byte signature + 4-byte length + "IHDR" + width/height),
/// not a full decode - the grid wants size labels, not pixel data.
/// </summary>
public sealed record ScreenshotEntry
{
    /// <summary>Absolute path to the PNG file.</summary>
    public required string FullPath { get; init; }

    /// <summary>File name (no directory). Drives the tile tooltip.</summary>
    public required string Filename { get; init; }

    /// <summary>UTC last-write timestamp - Minecraft writes the screenshot the moment it's saved, so mtime tracks the shot time well enough.</summary>
    public required DateTimeOffset TakenAt { get; init; }

    /// <summary>File length in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Image width in pixels. <c>0</c> when the IHDR was unreadable.</summary>
    public int Width { get; init; }

    /// <summary>Image height in pixels. <c>0</c> when the IHDR was unreadable.</summary>
    public int Height { get; init; }
}
