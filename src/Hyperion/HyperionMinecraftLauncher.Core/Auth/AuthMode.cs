namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

/// <summary>How the launcher should resolve the user's session.</summary>
public enum AuthMode
{
    /// <summary>Offline session — no remote auth, server access is limited to LAN / offline-mode servers.</summary>
    Offline,

    /// <summary>Microsoft account authentication. Requires Microsoft to issue a Client ID; documented limitation in v0.1.</summary>
    Microsoft,

    /// <summary>Legacy Mojang username + password (removed from the official Mojang API in 2022). Kept for completeness.</summary>
    Mojang,
}
