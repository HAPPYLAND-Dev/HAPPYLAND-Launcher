namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// Quick Play target for a launch. Minecraft 1.20+ supports launch-time deep links
/// into a world or server via dedicated game arguments. The launcher exposes the
/// three branches the official launcher uses: <see cref="None"/> (regular launch
/// into the main menu), <see cref="Singleplayer"/> (open a specific world by its
/// folder name under <c>saves/</c>), and <see cref="Multiplayer"/> (join a server
/// by host and port).
/// </summary>
/// <remarks>
/// Modelled as a closed discriminated union so callers can pattern-match without
/// stringly-typed mode flags. Default for <see cref="LaunchRequest.QuickPlay"/> is
/// <see cref="None"/>, matching every existing launch path.
/// </remarks>
public abstract record QuickPlay
{
    /// <summary>Private constructor closes the hierarchy to the nested cases.</summary>
    private protected QuickPlay() { }

    /// <summary>No Quick Play target - launch into the main menu as usual.</summary>
    public sealed record None : QuickPlay;

    /// <summary>Open the given world folder (relative to <c>&lt;gameDir&gt;/saves/</c>) on launch.</summary>
    /// <param name="WorldFolderName">The save folder name as it appears on disk. Not validated here.</param>
    public sealed record Singleplayer(string WorldFolderName) : QuickPlay;

    /// <summary>Connect to the given server on launch.</summary>
    /// <param name="Host">Server hostname or IP (e.g. <c>"mc.hypixel.net"</c>).</param>
    /// <param name="Port">TCP port. Defaults to the Minecraft default (25565).</param>
    public sealed record Multiplayer(string Host, int Port = 25565) : QuickPlay;
}
