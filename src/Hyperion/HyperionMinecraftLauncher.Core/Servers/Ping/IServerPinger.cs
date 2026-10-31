using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

/// <summary>
/// Pings a Minecraft server using the modern Server List Ping (SLP) protocol.
/// Implementations return <c>null</c> when the server is unreachable, the connection
/// times out, or the response cannot be parsed - callers render that as "offline".
/// </summary>
public interface IServerPinger
{
    /// <summary>
    /// Ping <paramref name="host"/>:<paramref name="port"/>, giving up after
    /// <paramref name="timeout"/>. Returns the parsed status (with latency) or
    /// <c>null</c> on any failure.
    /// </summary>
    Task<ServerStatus?> PingAsync(string host, int port, TimeSpan timeout, CancellationToken ct);
}
