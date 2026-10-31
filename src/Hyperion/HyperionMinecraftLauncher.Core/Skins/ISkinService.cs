using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

/// <summary>
/// Writes and reads the signed-in player's skin + cape via the Mojang
/// <c>api.minecraftservices.com/minecraft/profile/*</c> endpoints.
/// </summary>
/// <remarks>
/// Every method requires a Minecraft Services bearer token (the <c>AccessToken</c>
/// returned by Microsoft sign-in). Passing a null / empty token raises a
/// <see cref="Launcher.LauncherException"/> subclass without hitting the network.
/// </remarks>
public interface ISkinService
{
    /// <summary>
    /// Upload a 64x64 (or legacy 64x32) PNG and set it as the player's active skin.
    /// Calls <c>POST https://api.minecraftservices.com/minecraft/profile/skins</c> as
    /// multipart/form-data with the <c>variant</c> ("classic"|"slim") and the file body.
    /// </summary>
    Task UploadSkinAsync(string accessToken, byte[] pngBytes, SkinVariant variant, CancellationToken cancellationToken);

    /// <summary>
    /// Fetch the current player profile (UUID, display name, owned skins + capes).
    /// Calls <c>GET https://api.minecraftservices.com/minecraft/profile</c>.
    /// </summary>
    Task<PlayerProfile> GetProfileAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Set one of the player's owned capes as active.
    /// Calls <c>PUT https://api.minecraftservices.com/minecraft/profile/capes/active</c>
    /// with body <c>{"capeId":"..."}</c>.
    /// </summary>
    Task SetActiveCapeAsync(string accessToken, string capeId, CancellationToken cancellationToken);

    /// <summary>
    /// Hide the currently-active cape (player keeps ownership; nothing rendered in game).
    /// Calls <c>DELETE https://api.minecraftservices.com/minecraft/profile/capes/active</c>.
    /// </summary>
    Task ClearActiveCapeAsync(string accessToken, CancellationToken cancellationToken);
}
