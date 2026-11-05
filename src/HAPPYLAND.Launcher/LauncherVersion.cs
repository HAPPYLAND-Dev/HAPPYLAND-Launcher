using System.Globalization;
using System.Reflection;

namespace Happyland.Launcher;

internal static class LauncherVersion
{
    public static long Code { get; } = long.Parse(typeof(LauncherVersion).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "LauncherVersionCode").Value!,
        CultureInfo.InvariantCulture);
}
