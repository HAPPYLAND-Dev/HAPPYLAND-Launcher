namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;

/// <summary>
/// One row from <c>servers.dat</c> - the file the in-game multiplayer screen reads and writes.
/// We surface display name, host:port, and the optional 64x64 favicon (base64 PNG, no <c>data:</c> prefix).
/// </summary>
public sealed record ServerListEntry
{
    /// <summary>Display name (may contain Minecraft section-sign formatting codes like <c>"&#xA7;cRed"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Host or <c>"host:port"</c>.</summary>
    public required string Ip { get; init; }

    /// <summary>Base64-encoded 64x64 PNG favicon, without the <c>data:image/png;base64,</c> prefix. <c>null</c> when absent.</summary>
    public string? IconBase64 { get; init; }

    /// <summary>The "accept server textures" client preference: <c>true</c> = always, <c>false</c> = never, <c>null</c> = prompt every join.</summary>
    public bool? AcceptTextures { get; init; }
}
