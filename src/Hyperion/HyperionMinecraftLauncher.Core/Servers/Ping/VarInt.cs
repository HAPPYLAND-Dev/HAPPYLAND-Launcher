using System;
using System.IO;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

/// <summary>
/// Minecraft-protocol VarInt encoder/decoder. The format is the same little-endian
/// base-128 varint Protocol Buffers uses, but Minecraft treats the input as a signed
/// 32-bit integer; <c>-1</c> therefore encodes as five bytes <c>0xFF 0xFF 0xFF 0xFF 0x0F</c>.
/// See <a href="https://minecraft.wiki/w/Java_Edition_protocol#VarInt_and_VarLong">wiki/Java_Edition_protocol</a>.
/// Public so tests and any future consumer (mod-protocol bridge, fake server) can reuse it.
/// </summary>
public static class VarInt
{
    private const int SegmentMask = 0x7F;
    private const int ContinueBit = 0x80;
    private const int MaxBytes = 5;

    /// <summary>Write <paramref name="value"/> to <paramref name="stream"/> as a VarInt.</summary>
    public static void Write(Stream stream, int value)
    {
        // Reinterpret as uint so the shift fills with zeros - the "logical" right shift
        // every reference implementation uses.
        var bits = unchecked((uint)value);
        while (true)
        {
            if ((bits & ~(uint)SegmentMask) == 0)
            {
                stream.WriteByte((byte)bits);
                return;
            }
            stream.WriteByte((byte)((bits & SegmentMask) | ContinueBit));
            bits >>= 7;
        }
    }

    /// <summary>
    /// Read a VarInt from <paramref name="stream"/>. Throws <see cref="EndOfStreamException"/>
    /// if the stream ends mid-VarInt, <see cref="InvalidDataException"/> if the encoded value
    /// is longer than 5 bytes (out-of-range for the Java-edition protocol).
    /// </summary>
    public static int Read(Stream stream)
    {
        int result = 0;
        int shift = 0;
        for (int i = 0; i < MaxBytes; i++)
        {
            int b = stream.ReadByte();
            if (b < 0)
                throw new EndOfStreamException("Stream ended mid-VarInt.");
            result |= (b & SegmentMask) << shift;
            if ((b & ContinueBit) == 0)
                return result;
            shift += 7;
        }
        throw new InvalidDataException("VarInt is longer than 5 bytes.");
    }

    /// <summary>Encode <paramref name="value"/> to a fresh byte array. Convenience for prefixing packets.</summary>
    public static byte[] Encode(int value)
    {
        using var ms = new MemoryStream(MaxBytes);
        Write(ms, value);
        return ms.ToArray();
    }
}
