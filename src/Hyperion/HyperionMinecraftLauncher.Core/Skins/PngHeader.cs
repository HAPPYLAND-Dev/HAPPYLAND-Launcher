using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

/// <summary>
/// Small helper for sanity-checking PNG byte buffers before sending them to the Mojang
/// skin-upload endpoint. Keeps the file-picker code in the App project free of magic
/// numbers and gives the test project a public surface to verify.
/// </summary>
public static class PngHeader
{
    /// <summary>The 8-byte PNG signature (137 80 78 71 13 10 26 10).</summary>
    public static readonly byte[] Signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>True when the first 8 bytes of <paramref name="data"/> match the PNG signature.</summary>
    public static bool HasValidSignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < Signature.Length) return false;
        for (int i = 0; i < Signature.Length; i++)
            if (data[i] != Signature[i]) return false;
        return true;
    }

    /// <summary>True when <paramref name="data"/> looks like a valid PNG and weighs at most <paramref name="maxBytes"/> bytes.</summary>
    public static bool IsAcceptableSkinPng(ReadOnlySpan<byte> data, int maxBytes = 32 * 1024)
        => data.Length > 0 && data.Length <= maxBytes && HasValidSignature(data);
}
