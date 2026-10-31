using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using fNbt;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;

/// <summary>
/// Parses <c>servers.dat</c> via <see cref="NbtFile"/>. The file is uncompressed NBT; the root
/// is an unnamed compound with one <see cref="NbtList"/> child named <c>"servers"</c> whose
/// entries are compounds with <c>name</c>, <c>ip</c>, optional <c>icon</c>, optional
/// <c>acceptTextures</c>.
/// </summary>
public sealed class FileServersStore : IServersStore
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ServerListEntry>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
            return Task.FromResult<IReadOnlyList<ServerListEntry>>(Array.Empty<ServerListEntry>());

        // fNbt is synchronous; wrap in Task.Run so callers can hand us a CancellationToken
        // without us blocking their UI thread on the disk read.
        return Task.Run<IReadOnlyList<ServerListEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var file = new NbtFile();
                file.LoadFromFile(path);

                var serversTag = file.RootTag.Get<NbtList>("servers");
                if (serversTag is null)
                    return Array.Empty<ServerListEntry>();

                var list = new List<ServerListEntry>(serversTag.Count);
                foreach (var tag in serversTag)
                {
                    if (tag is not NbtCompound entry)
                        continue;

                    list.Add(new ServerListEntry
                    {
                        Name = entry.Get<NbtString>("name")?.Value ?? string.Empty,
                        Ip = entry.Get<NbtString>("ip")?.Value ?? string.Empty,
                        IconBase64 = entry.Get<NbtString>("icon")?.Value,
                        AcceptTextures = entry.Get<NbtByte>("acceptTextures") is { Value: var v }
                            ? v != 0
                            : (bool?)null,
                    });
                }
                return list;
            }
            catch (Exception)
            {
                // Defensive: a corrupt servers.dat shouldn't crash the launcher; treat as empty.
                return Array.Empty<ServerListEntry>();
            }
        }, cancellationToken);
    }
}
