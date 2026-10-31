using System;
using System.Text;
using System.Text.Json;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

/// <summary>
/// Parser for the JSON body of a Minecraft server's Status Response (packet 0x00 reply).
/// Tolerates the two MOTD shapes the wild encounters: a plain string (legacy 1.6-) and a
/// rich-text component object with optional <c>extra[]</c> children (modern). Strips section-sign
/// formatting codes (<c>&#xA7;a</c> etc.) so callers can render a flat string.
/// </summary>
public static class ServerStatusJson
{
    /// <summary>
    /// Parse <paramref name="json"/> into a <see cref="ServerStatus"/>; <paramref name="latencyMs"/>
    /// is the externally-measured round-trip from the 0x01 ping/pong. Returns <c>null</c> when the
    /// JSON is malformed, empty, or missing the minimum fields - callers treat that as "unreachable".
    /// </summary>
    public static ServerStatus? Parse(string json, long latencyMs)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            string? versionName = null;
            int protocol = 0;
            if (root.TryGetProperty("version", out var versionEl) && versionEl.ValueKind == JsonValueKind.Object)
            {
                if (versionEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    versionName = nameEl.GetString();
                if (versionEl.TryGetProperty("protocol", out var protoEl) && protoEl.ValueKind == JsonValueKind.Number)
                    protocol = protoEl.GetInt32();
            }

            int online = 0, max = 0;
            if (root.TryGetProperty("players", out var playersEl) && playersEl.ValueKind == JsonValueKind.Object)
            {
                if (playersEl.TryGetProperty("online", out var onlineEl) && onlineEl.ValueKind == JsonValueKind.Number)
                    online = onlineEl.GetInt32();
                if (playersEl.TryGetProperty("max", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number)
                    max = maxEl.GetInt32();
            }

            string motd = string.Empty;
            if (root.TryGetProperty("description", out var descEl))
                motd = StripFormatting(ExtractText(descEl));

            byte[]? favicon = null;
            if (root.TryGetProperty("favicon", out var faviconEl) && faviconEl.ValueKind == JsonValueKind.String)
                favicon = DecodeFavicon(faviconEl.GetString());

            // A response that didn't even carry version/players is almost certainly garbage.
            if (versionName is null && online == 0 && max == 0 && motd.Length == 0)
                return null;

            return new ServerStatus
            {
                Version = versionName,
                Protocol = protocol,
                OnlinePlayers = online,
                MaxPlayers = max,
                Motd = motd,
                FaviconPng = favicon,
                LatencyMs = latencyMs,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractText(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                return el.GetString() ?? string.Empty;
            case JsonValueKind.Object:
            {
                var sb = new StringBuilder();
                if (el.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                    sb.Append(textEl.GetString());
                if (el.TryGetProperty("extra", out var extraEl) && extraEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in extraEl.EnumerateArray())
                        sb.Append(ExtractText(child));
                }
                return sb.ToString();
            }
            case JsonValueKind.Array:
            {
                var sb = new StringBuilder();
                foreach (var child in el.EnumerateArray())
                    sb.Append(ExtractText(child));
                return sb.ToString();
            }
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Strip <c>&#xA7;X</c> formatting codes (and the rarer ampersand variant) from a MOTD string.
    /// Anything following a section sign is dropped (one char), regardless of which code letter it is.
    /// </summary>
    private static string StripFormatting(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '§' && i + 1 < s.Length)
            {
                // Skip the section sign and the following code character.
                i++;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static byte[]? DecodeFavicon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        const string prefix = "data:image/png;base64,";
        // T18: keep the data-URI strip on a ReadOnlySpan<char> so we don't allocate the
        // intermediate string from Substring; Convert.TryFromBase64Chars consumes the span
        // directly into a rented buffer sized to the maximum possible decoded length.
        ReadOnlySpan<char> source = value.AsSpan();
        if (source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            source = source[prefix.Length..];

        // Base64 encodes 3 bytes per 4 chars; round up to a safe upper bound.
        var maxDecoded = (source.Length / 4 + 1) * 3;
        var buffer = new byte[maxDecoded];
        if (Convert.TryFromBase64Chars(source, buffer, out var bytesWritten))
        {
            if (bytesWritten == buffer.Length) return buffer;
            // Trim the over-allocated tail so the returned array is the exact decoded size.
            var trimmed = new byte[bytesWritten];
            Buffer.BlockCopy(buffer, 0, trimmed, 0, bytesWritten);
            return trimmed;
        }
        return null;
    }
}
