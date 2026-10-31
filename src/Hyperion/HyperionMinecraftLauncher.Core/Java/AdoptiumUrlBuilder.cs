using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

/// <summary>
/// Builds Adoptium Temurin "latest GA" binary URLs for a given <see cref="JavaRequirement"/>
/// and (os, arch) pair. Endpoint shape and parameter strings are documented at
/// https://api.adoptium.net/q/swagger-ui/.
/// </summary>
public static class AdoptiumUrlBuilder
{
    private const string ApiRoot = "https://api.adoptium.net/v3/binary/latest";

    /// <summary>
    /// Build the binary-download URL for the given runtime + platform tuple. The URL hits
    /// Adoptium's "latest GA JRE" redirect, which 302s to a CDN-hosted archive (zip on Windows,
    /// tar.gz elsewhere).
    /// </summary>
    /// <param name="requirement">The Java feature version to pull.</param>
    /// <param name="os">Adoptium-style OS string: <c>windows</c>, <c>linux</c>, or <c>mac</c>.</param>
    /// <param name="arch">Adoptium-style architecture string: <c>x64</c>, <c>aarch64</c>, etc.</param>
    public static string BuildBinaryUrl(JavaRequirement requirement, string os, string arch)
    {
        if (string.IsNullOrWhiteSpace(os))
            throw new ArgumentException("os is required", nameof(os));
        if (string.IsNullOrWhiteSpace(arch))
            throw new ArgumentException("arch is required", nameof(arch));

        var feature = FeatureNumber(requirement);
        return $"{ApiRoot}/{feature}/ga/{os.Trim()}/{arch.Trim()}/jre/hotspot/normal/eclipse";
    }

    /// <summary>The numeric "feature version" Adoptium wants in its path.</summary>
    public static int FeatureNumber(JavaRequirement requirement) => requirement switch
    {
        JavaRequirement.Java8 => 8,
        JavaRequirement.Java17 => 17,
        JavaRequirement.Java21 => 21,
        JavaRequirement.Java25 => 25,
        _ => throw new ArgumentOutOfRangeException(nameof(requirement), requirement, "Unknown JavaRequirement"),
    };
}
