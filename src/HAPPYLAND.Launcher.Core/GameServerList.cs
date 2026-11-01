using fNbt;
using Happyland.Contracts;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace Happyland.Launcher.Core;

public static class GameServerList
{
    public static Task SynchronizeAsync(string gameDirectory, IReadOnlyList<GameServer> servers, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(gameDirectory, "servers.dat");
        var file = File.Exists(path) ? new NbtFile(path) : new NbtFile(new NbtCompound(""));
        var existing = file.RootTag.Get<NbtList>("servers")?.Cast<NbtCompound>().ToList() ?? [];
        var updated = new NbtList("servers", NbtTagType.Compound);
        foreach (var server in servers)
        foreach (var route in server.GetRoutes())
        {
            var address = CmlLibUnderlyingLauncher.BuildQuickPlayArgs(new QuickPlay.Multiplayer(route.Host, route.Port))[1];
            var previous = existing.FirstOrDefault(entry => SameAddress(entry.Get<NbtString>("ip")!.Value, route));
            var entry = previous is null ? new NbtCompound() : new NbtCompound(previous);
            entry["name"] = new NbtString("name", $"{server.Name}（{route.Name}）");
            entry["ip"] = new NbtString("ip", address);
            entry["acceptTextures"] = new NbtByte("acceptTextures", 1);
            entry.Remove("hidden");
            updated.Add(entry);
            existing.RemoveAll(item => SameAddress(item.Get<NbtString>("ip")!.Value, route));
        }
        foreach (var entry in existing) updated.Add(new NbtCompound(entry));
        file.RootTag["servers"] = updated;
        Directory.CreateDirectory(gameDirectory);
        var staged = path + ".happyland.tmp";
        file.SaveToFile(staged, NbtCompression.None);
        ct.ThrowIfCancellationRequested();
        File.Move(staged, path, overwrite: true);
    }, ct);

    private static bool SameAddress(string address, ServerRoute route)
    {
        var (host, port) = CmlLibMinecraftLauncherService.ParseHostPort(address);
        return string.Equals(host, route.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase) && port == route.Port;
    }
}
