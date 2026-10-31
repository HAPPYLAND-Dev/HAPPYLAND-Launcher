using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

/// <summary>
/// Maps a Minecraft version id (release or snapshot) onto the minimum
/// <see cref="JavaRequirement"/> the game needs to run.
///
/// Cutoffs (from Mojang's published JRE bumps):
///   - 1.20.5+         -> <see cref="JavaRequirement.Java21"/>
///   - 1.17 .. 1.20.4  -> <see cref="JavaRequirement.Java17"/>
///   - 1.16.x and older -> <see cref="JavaRequirement.Java8"/>
///
/// Snapshots like <c>"23w14a"</c> are mapped to the closest known release year/season.
/// Unknown / malformed strings default to <see cref="JavaRequirement.Java21"/> (the
/// current bleeding edge - safer to over-install JRE than to fail with a cryptic
/// "wrong Java" crash in the game process).
/// </summary>
public static class JavaRequirementResolver
{
    /// <summary>
    /// Resolve the minimum Java requirement for the given Minecraft version id.
    /// </summary>
    public static JavaRequirement For(string minecraftVersion)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return JavaRequirement.Java21;

        var v = minecraftVersion.Trim();

        // Snapshot id pattern: "YYwWWX" (e.g. "23w14a"). Use the calendar year/week to bracket
        // the snapshot against known release cycles. We use the year and the week number rather
        // than trying to find the exact targeted release - the mapping is "good enough" to pick
        // the right JRE family.
        if (TryParseSnapshot(v, out var year, out var week))
        {
            // 24wXX onwards target 1.20.5+ / 1.21+ -> Java 21.
            // 24w14a was the first snapshot that required Java 21 (around 1.20.5).
            if (year >= 25) return JavaRequirement.Java21;
            if (year == 24)
            {
                // 24w03 was last in 1.20.4 cycle; 24w14a started 1.20.5.
                // Use the cutoff at w14.
                return week >= 14 ? JavaRequirement.Java21 : JavaRequirement.Java17;
            }
            // 21..23 target 1.17 - 1.20.4 -> Java 17.
            if (year >= 21) return JavaRequirement.Java17;
            // 20 and older target 1.16 cycle or earlier -> Java 8.
            return JavaRequirement.Java8;
        }

        // Strip pre-release / release-candidate suffixes like "-rc1", "-pre1", " Pre-Release 3".
        var bare = StripPreReleaseSuffix(v);

        // Try a strict numeric parse of "major.minor[.patch]".
        if (!TryParseRelease(bare, out var minor, out var patch))
        {
            // Couldn't parse - assume bleeding edge.
            return JavaRequirement.Java21;
        }

        // 1.20.5+ -> Java 21.
        if (minor > 20) return JavaRequirement.Java21;
        if (minor == 20 && patch >= 5) return JavaRequirement.Java21;

        // 1.17 .. 1.20.4 -> Java 17.
        if (minor >= 17) return JavaRequirement.Java17;

        // 1.16.x and older -> Java 8.
        return JavaRequirement.Java8;
    }

    /// <summary>
    /// Try parsing a release id of the form <c>"1.MINOR[.PATCH][...]"</c>. Trailing junk
    /// after the patch number is tolerated so combined ids like <c>"1.20.5-OptiFine"</c>
    /// still resolve correctly.
    /// </summary>
    private static bool TryParseRelease(string s, out int minor, out int patch)
    {
        minor = 0;
        patch = 0;

        // Must start with "1."; modern Minecraft is all "1.x".
        if (!s.StartsWith("1.", StringComparison.Ordinal)) return false;

        var rest = s.AsSpan(2);
        var dot = rest.IndexOf('.');
        ReadOnlySpan<char> minorSpan;
        ReadOnlySpan<char> patchSpan = ReadOnlySpan<char>.Empty;
        if (dot < 0)
        {
            minorSpan = TakeLeadingDigits(rest, out _);
        }
        else
        {
            minorSpan = TakeLeadingDigits(rest[..dot], out _);
            patchSpan = TakeLeadingDigits(rest[(dot + 1)..], out _);
        }

        if (minorSpan.IsEmpty) return false;
        if (!int.TryParse(minorSpan, out minor)) return false;
        if (!patchSpan.IsEmpty)
        {
            // Patch may be missing or non-numeric; treat non-numeric as 0.
            int.TryParse(patchSpan, out patch);
        }
        return true;
    }

    /// <summary>Return the leading run of digit chars in <paramref name="s"/>.</summary>
    private static ReadOnlySpan<char> TakeLeadingDigits(ReadOnlySpan<char> s, out int consumed)
    {
        var i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        consumed = i;
        return s[..i];
    }

    /// <summary>
    /// Try parsing a snapshot id of the form <c>"YYwWWX"</c> (e.g. <c>"23w14a"</c>).
    /// </summary>
    private static bool TryParseSnapshot(string s, out int year, out int week)
    {
        year = 0;
        week = 0;
        if (s.Length < 5) return false;
        if (!char.IsDigit(s[0]) || !char.IsDigit(s[1])) return false;
        if (s[2] != 'w' && s[2] != 'W') return false;
        if (!char.IsDigit(s[3]) || !char.IsDigit(s[4])) return false;
        year = (s[0] - '0') * 10 + (s[1] - '0');
        week = (s[3] - '0') * 10 + (s[4] - '0');
        return true;
    }

    /// <summary>
    /// Strip well-known pre-release / RC suffixes so the version id collapses back to its
    /// base release for cutoff matching.
    /// </summary>
    private static string StripPreReleaseSuffix(string s)
    {
        // Common forms: "1.20.5-rc1", "1.17-pre3", "1.20.5 Pre-Release 1".
        // Cut at the first '-' or ' ' so the numeric part is intact.
        var dash = s.IndexOf('-');
        var space = s.IndexOf(' ');
        var cut = dash >= 0 && (space < 0 || dash < space) ? dash :
                  space >= 0 ? space : -1;
        return cut > 0 ? s[..cut] : s;
    }
}
