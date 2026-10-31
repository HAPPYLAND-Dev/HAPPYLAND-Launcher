using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

/// <summary>Fetches a player's skin + cape from a Minecraft profile UUID.</summary>
public interface IPlayerSkinFetcher
{
    /// <summary>
    /// Resolve the textures attached to the given player UUID and download the PNGs.
    /// Returns <c>null</c> on network failure or when the profile has no skin (the caller
    /// keeps showing the default Steve skin).
    /// </summary>
    Task<PlayerSkinInfo?> FetchAsync(string uuid, CancellationToken cancellationToken);
}
