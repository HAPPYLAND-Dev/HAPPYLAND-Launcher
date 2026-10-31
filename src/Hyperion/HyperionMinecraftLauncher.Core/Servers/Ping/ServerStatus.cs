namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

/// <summary>
/// Result of a Server List Ping. Mirrors the public fields the modern Minecraft status
/// JSON exposes (<c>version</c>, <c>players</c>, <c>description</c>, optional <c>favicon</c>)
/// plus a round-trip latency measurement taken from the 0x01 ping/pong packet.
/// </summary>
public sealed record ServerStatus
{
    /// <summary>Human-readable server version (for example <c>"1.20.4"</c>); may be null if the server omitted it.</summary>
    public string? Version { get; init; }

    /// <summary>Protocol number the server advertises (765 for 1.20.4, 47 for 1.8, etc.). 0 when unknown.</summary>
    public int Protocol { get; init; }

    /// <summary>Players currently online.</summary>
    public int OnlinePlayers { get; init; }

    /// <summary>Player cap.</summary>
    public int MaxPlayers { get; init; }

    /// <summary>MOTD with section-sign formatting codes stripped (flat plain text).</summary>
    public string Motd { get; init; } = string.Empty;

    /// <summary>Decoded 64x64 PNG favicon (if any) - already stripped of the <c>data:image/png;base64,</c> prefix.</summary>
    public byte[]? FaviconPng { get; init; }

    /// <summary>Round-trip latency from the 0x01 ping/pong, in milliseconds.</summary>
    public long LatencyMs { get; init; }
}
