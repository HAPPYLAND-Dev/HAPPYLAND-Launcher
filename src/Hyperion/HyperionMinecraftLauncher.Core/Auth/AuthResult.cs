namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

/// <summary>
/// The result of an <see cref="IMinecraftLauncherService.AuthenticateAsync"/> call,
/// containing everything <see cref="Launcher.LaunchRequest"/> needs to actually start the game.
/// </summary>
public sealed record AuthResult
{
    /// <summary>Display name that will appear in-game.</summary>
    public required string Username { get; init; }

    /// <summary>Player UUID (Java edition format, no dashes).</summary>
    public required string Uuid { get; init; }

    /// <summary>Access token. For offline sessions this is the literal <c>"0"</c> CmlLib uses.</summary>
    public required string AccessToken { get; init; }

    /// <summary>True when the session was produced offline (no remote token issued).</summary>
    public required bool IsOffline { get; init; }
}
