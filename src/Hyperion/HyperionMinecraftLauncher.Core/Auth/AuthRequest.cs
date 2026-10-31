namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

/// <summary>
/// Identity-supply request. The MVP supports the offline path; Microsoft / Mojang
/// paths are reserved for v0.2.
/// </summary>
public sealed record AuthRequest
{
    /// <summary>Authentication mode to use.</summary>
    public required AuthMode Mode { get; init; }

    /// <summary>
    /// Desired username. For offline mode this is the player's chosen name;
    /// for online modes it is informational only (the auth provider supplies it).
    /// </summary>
    public required string Username { get; init; }
}
