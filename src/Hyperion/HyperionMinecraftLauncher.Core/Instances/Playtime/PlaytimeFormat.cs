using System;
using System.Globalization;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Playtime;

/// <summary>Short play-time label: "{m} min" under an hour, "{h} h {m} min" from there on.</summary>
public static class PlaytimeFormat
{
    /// <param name="value">Time played.</param>
    /// <param name="hoursMinutesFormat">Localized format with {0}=hours, {1}=minutes.</param>
    /// <param name="minutesFormat">Localized format with {0}=minutes.</param>
    public static string Format(TimeSpan value, string hoursMinutesFormat, string minutesFormat)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        var hours = (long)value.TotalHours;
        return hours == 0
            ? string.Format(CultureInfo.CurrentCulture, minutesFormat, value.Minutes)
            : string.Format(CultureInfo.CurrentCulture, hoursMinutesFormat, hours, value.Minutes);
    }
}
