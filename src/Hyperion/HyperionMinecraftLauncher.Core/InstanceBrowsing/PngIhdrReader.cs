using System;
using System.IO;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// Reads the first 24 bytes of a PNG to extract width/height from the IHDR chunk. The PNG
/// spec mandates IHDR as the first chunk after the 8-byte signature, so we never have to
/// walk the rest of the file. Saves us from decompressing IDAT just to label a thumbnail.
/// </summary>
public static class PngIhdrReader
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// Read width/height from the PNG IHDR chunk at <paramref name="path"/>. Returns
    /// <c>false</c> for missing files, non-PNGs, or corrupt headers; never throws.
    /// </summary>
    public static bool TryReadSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            // 8 byte signature + 4 byte chunk length + 4 byte "IHDR" + 4 byte width + 4 byte height = 24 bytes.
            Span<byte> header = stackalloc byte[24];
            int read = 0;
            while (read < header.Length)
            {
                var n = fs.Read(header[read..]);
                if (n <= 0) return false;
                read += n;
            }

            for (int i = 0; i < PngSignature.Length; i++)
                if (header[i] != PngSignature[i]) return false;

            // The chunk type at offset 12..16 must be "IHDR".
            if (header[12] != (byte)'I' || header[13] != (byte)'H'
                || header[14] != (byte)'D' || header[15] != (byte)'R')
                return false;

            width = ReadBigEndianInt(header, 16);
            height = ReadBigEndianInt(header, 20);
            return width > 0 && height > 0;
        }
        catch
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    private static int ReadBigEndianInt(ReadOnlySpan<byte> span, int offset)
    {
        return (span[offset] << 24)
             | (span[offset + 1] << 16)
             | (span[offset + 2] << 8)
             | span[offset + 3];
    }
}
