using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Updates;

/// <summary>
/// Tiny three-part semver comparator. Strictly accepts <c>X.Y.Z</c> (optionally preceded by
/// <c>v</c>); anything else - empty, two-part, suffix-tagged - is treated as unparseable.
/// </summary>
/// <remarks>
/// Hyperion ships with a 3-part SemVer policy (see README); a full semver-2.0 parser would
/// be overkill. We strip a leading <c>v</c> because GitHub release tag_name conventions vary.
/// </remarks>
public static class SemverComparer
{
    /// <summary>
    /// True if <paramref name="a"/> is strictly newer than <paramref name="b"/>. Equal versions
    /// return false. Unparseable inputs return false (never throw, never claim "newer").
    /// </summary>
    public static bool IsNewerThan(string a, string b)
    {
        if (!TryParse(a, out var av)) return false;
        if (!TryParse(b, out var bv)) return false;

        if (av.major != bv.major) return av.major > bv.major;
        if (av.minor != bv.minor) return av.minor > bv.minor;
        return av.patch > bv.patch;
    }

    private static bool TryParse(string? raw, out (int major, int minor, int patch) parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var s = raw.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            s = s.Substring(1);

        var parts = s.Split('.');
        if (parts.Length != 3) return false;

        if (!int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var maj)) return false;
        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var min)) return false;
        if (!int.TryParse(parts[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var pat)) return false;
        if (maj < 0 || min < 0 || pat < 0) return false;

        parsed = (maj, min, pat);
        return true;
    }
}
