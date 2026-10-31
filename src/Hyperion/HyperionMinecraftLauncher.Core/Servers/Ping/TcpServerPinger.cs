using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net;
using DnsClient;
using DnsClient.Protocol;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

/// <summary>
/// Modern Minecraft Server List Ping over TCP. Sequence (all packets are length-prefixed VarInts
/// wrapping a VarInt packet-id then payload):
/// <list type="number">
///   <item>C-&gt;S Handshake (id 0x00): protocol version, server address, server port, next state=1 (status).</item>
///   <item>C-&gt;S Status Request (id 0x00): empty body.</item>
///   <item>S-&gt;C Status Response (id 0x00): one VarInt-prefixed UTF-8 JSON string.</item>
///   <item>C-&gt;S Ping (id 0x01): 8-byte long payload (we send <c>Stopwatch.GetTimestamp()</c>).</item>
///   <item>S-&gt;C Pong (id 0x01): echoes the long back. We measure latency around this round trip.</item>
/// </list>
/// Failures return <c>null</c> - the UI surfaces that as the "unreachable" grey dot.
/// </summary>
public sealed class TcpServerPinger : IServerPinger
{
    /// <summary>Vanilla port the server defaults to when the user typed only a hostname.</summary>
    public const int DefaultPort = 25565;

    /// <summary>1.8 protocol number. Servers honor any reasonable value; using a low one widens compatibility.</summary>
    public const int DefaultProtocolVersion = 47;

    private readonly int _protocolVersion;
    private static readonly LookupClient Dns = new(new LookupClientOptions { Timeout = TimeSpan.FromSeconds(2), Retries = 0, UseCache = true });

    /// <summary>Construct with the optional handshake protocol version (defaults to 1.8 = 47).</summary>
    public TcpServerPinger(int protocolVersion = DefaultProtocolVersion)
    {
        _protocolVersion = protocolVersion;
    }

    /// <inheritdoc />
    public async Task<ServerStatus?> PingAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;
        if (port <= 0 || port > 65535)
            port = DefaultPort;

        // Compose a CTS so we obey both the caller's cancel token and our own timeout.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        var token = linked.Token;

        try
        {
            using var client = new TcpClient();
            var connectHost = host;
            var connectPort = port;
            if (port == DefaultPort && !IPAddress.TryParse(host, out _))
            {
                try
                {
                    var response = await Dns.QueryAsync("_minecraft._tcp." + host.TrimEnd('.'), QueryType.SRV, cancellationToken: token).ConfigureAwait(false);
                    var records = response.Answers.SrvRecords().OrderBy(r => r.Priority).ToArray();
                    if (records.Length > 0)
                    {
                        var candidates = records.Where(r => r.Priority == records[0].Priority).ToArray();
                        var totalWeight = candidates.Sum(r => (int)r.Weight);
                        var selected = candidates[Random.Shared.Next(candidates.Length)];
                        if (totalWeight > 0)
                        {
                            var pick = Random.Shared.Next(totalWeight);
                            foreach (var candidate in candidates)
                            {
                                pick -= candidate.Weight;
                                if (pick < 0) { selected = candidate; break; }
                            }
                        }
                        if (selected.Target.Value == ".") return null;
                        connectHost = selected.Target.Value.TrimEnd('.');
                        connectPort = selected.Port;
                    }
                }
                catch (DnsResponseException) { }
            }
            await client.ConnectAsync(connectHost, connectPort, token).ConfigureAwait(false);

            using var stream = client.GetStream();

            // ---- 1) Handshake (next state = 1 / status) ----
            await WritePacketAsync(stream, BuildHandshakePayload(host, port), token).ConfigureAwait(false);

            // ---- 2) Status Request (empty payload) ----
            await WritePacketAsync(stream, BuildStatusRequestPayload(), token).ConfigureAwait(false);

            // ---- 3) Read Status Response ----
            var jsonBody = await ReadStatusResponseAsync(stream, token).ConfigureAwait(false);
            if (jsonBody is null)
                return null;

            // ---- 4) Ping / 5) Pong - measure latency ----
            long latencyMs = await MeasurePingAsync(stream, token).ConfigureAwait(false);

            return ServerStatusJson.Parse(jsonBody, latencyMs);
        }
        catch (OperationCanceledException)
        {
            // Either the caller cancelled or our timeout fired - both render as "unreachable".
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private byte[] BuildHandshakePayload(string host, int port)
    {
        using var ms = new MemoryStream();
        VarInt.Write(ms, 0x00);                  // packet id
        VarInt.Write(ms, _protocolVersion);      // protocol version
        WriteString(ms, host);                   // server address (as the client typed it)
        WriteUShort(ms, (ushort)port);           // server port (big-endian)
        VarInt.Write(ms, 1);                     // next state: 1 = status
        return ms.ToArray();
    }

    private static byte[] BuildStatusRequestPayload()
    {
        using var ms = new MemoryStream(1);
        VarInt.Write(ms, 0x00);
        return ms.ToArray();
    }

    private static async Task WritePacketAsync(NetworkStream stream, byte[] payload, CancellationToken ct)
    {
        var prefix = VarInt.Encode(payload.Length);
        await stream.WriteAsync(prefix, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string?> ReadStatusResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        var packetLength = await ReadVarIntAsync(stream, ct).ConfigureAwait(false);
        if (packetLength <= 0 || packetLength > 1024 * 1024)
            return null;

        var buffer = ArrayPool<byte>.Shared.Rent(packetLength);
        try
        {
            await ReadExactAsync(stream, buffer.AsMemory(0, packetLength), ct).ConfigureAwait(false);

            using var packetStream = new MemoryStream(buffer, 0, packetLength, writable: false);
            int packetId = VarInt.Read(packetStream);
            if (packetId != 0x00)
                return null;

            int jsonLength = VarInt.Read(packetStream);
            if (jsonLength <= 0 || jsonLength > packetStream.Length - packetStream.Position)
                return null;

            // The JSON is the rest of this packet (modulo any trailing bytes some servers misbehave with).
            var jsonBytes = new byte[jsonLength];
            int read = packetStream.Read(jsonBytes, 0, jsonLength);
            if (read != jsonLength)
                return null;
            return Encoding.UTF8.GetString(jsonBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<long> MeasurePingAsync(NetworkStream stream, CancellationToken ct)
    {
        // 0x01 ping: a single big-endian Int64 payload. Use Stopwatch ticks so the response
        // round-trip we measure is monotonic and unaffected by wall-clock drift.
        long pingToken = Stopwatch.GetTimestamp();

        using (var pingBody = new MemoryStream(1 + 8))
        {
            VarInt.Write(pingBody, 0x01);
            WriteLong(pingBody, pingToken);
            await WritePacketAsync(stream, pingBody.ToArray(), ct).ConfigureAwait(false);
        }

        var sw = Stopwatch.StartNew();

        // Read pong.
        int pongPacketLength = await ReadVarIntAsync(stream, ct).ConfigureAwait(false);
        if (pongPacketLength <= 0 || pongPacketLength > 64)
            return 0;

        var buffer = ArrayPool<byte>.Shared.Rent(pongPacketLength);
        try
        {
            await ReadExactAsync(stream, buffer.AsMemory(0, pongPacketLength), ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken ct)
    {
        // Hand-rolled async VarInt - we cannot pre-buffer the whole packet because we don't know its size yet.
        int result = 0;
        int shift = 0;
        var single = new byte[1];
        for (int i = 0; i < 5; i++)
        {
            int read = await stream.ReadAsync(single.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Stream ended mid-VarInt.");
            int b = single[0];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
        }
        throw new InvalidDataException("VarInt is longer than 5 bytes.");
    }

    private static async Task ReadExactAsync(NetworkStream stream, Memory<byte> destination, CancellationToken ct)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = await stream.ReadAsync(destination[total..], ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Stream ended before payload completed.");
            total += read;
        }
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        VarInt.Write(stream, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteUShort(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteLong(Stream stream, long value)
    {
        // Big-endian, matches protocol.
        for (int i = 7; i >= 0; i--)
            stream.WriteByte((byte)((value >> (i * 8)) & 0xFF));
    }
}
