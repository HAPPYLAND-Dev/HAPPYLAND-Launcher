using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;

/// <summary>One run of Minecraft-formatted text. <see cref="Color"/> is <c>#RRGGBB</c>, or null for the default colour.</summary>
public sealed record McTextSpan(string Text, string? Color, bool Bold, bool Italic, bool Underlined, bool Strikethrough);

/// <summary>
/// Turns Minecraft text (the <c>pack.mcmeta</c> <c>description</c>) into styled spans: legacy
/// <c>§</c> codes inside strings, and JSON text components (<c>text</c> / <c>translate</c> with
/// <c>fallback</c>, <c>color</c>, bold / italic / underlined / strikethrough, <c>extra</c>, arrays).
/// Follows Java Edition rules: a colour code clears the formatting codes, <c>§r</c> resets to
/// the enclosing component's style, <c>§k</c> (obfuscated) is ignored.
/// </summary>
public static class McText
{
    private readonly record struct Style(string? Color, bool Bold, bool Italic, bool Underlined, bool Strikethrough);

    private static readonly Dictionary<char, string> LegacyColors = new()
    {
        ['0'] = "#000000", ['1'] = "#0000AA", ['2'] = "#00AA00", ['3'] = "#00AAAA",
        ['4'] = "#AA0000", ['5'] = "#AA00AA", ['6'] = "#FFAA00", ['7'] = "#AAAAAA",
        ['8'] = "#555555", ['9'] = "#5555FF", ['a'] = "#55FF55", ['b'] = "#55FFFF",
        ['c'] = "#FF5555", ['d'] = "#FF55FF", ['e'] = "#FFFF55", ['f'] = "#FFFFFF",
    };

    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "#000000", ["dark_blue"] = "#0000AA", ["dark_green"] = "#00AA00", ["dark_aqua"] = "#00AAAA",
        ["dark_red"] = "#AA0000", ["dark_purple"] = "#AA00AA", ["gold"] = "#FFAA00", ["gray"] = "#AAAAAA",
        ["dark_gray"] = "#555555", ["blue"] = "#5555FF", ["green"] = "#55FF55", ["aqua"] = "#55FFFF",
        ["red"] = "#FF5555", ["light_purple"] = "#FF55FF", ["yellow"] = "#FFFF55", ["white"] = "#FFFFFF",
    };

    /// <summary>Parse a <c>description</c> value: a string, a component object or an array of them.</summary>
    public static IReadOnlyList<McTextSpan> FromJson(JsonElement element)
    {
        var spans = new List<McTextSpan>();
        Walk(element, default, spans);
        return Merge(spans);
    }

    /// <summary>Parse a string that may contain legacy <c>§</c> codes.</summary>
    public static IReadOnlyList<McTextSpan> FromLegacy(string text)
    {
        var spans = new List<McTextSpan>();
        AppendLegacy(text ?? string.Empty, default, spans);
        return Merge(spans);
    }

    /// <summary>The text with all formatting removed.</summary>
    public static string ToPlainText(IEnumerable<McTextSpan> spans) => string.Concat(spans.Select(s => s.Text));

    private static void Walk(JsonElement el, Style inherited, List<McTextSpan> into)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                AppendLegacy(el.GetString() ?? string.Empty, inherited, into);
                break;
            case JsonValueKind.Array:
                foreach (var child in el.EnumerateArray())
                    Walk(child, inherited, into);
                break;
            case JsonValueKind.Object:
                var style = Apply(el, inherited);
                if (el.TryGetProperty("text", out var text))
                    AppendLegacy(text.ValueKind == JsonValueKind.String ? text.GetString() ?? string.Empty : text.ToString(), style, into);
                else if (el.TryGetProperty("translate", out var key))
                    // No translation tables here: show the fallback the pack ships, like the game does for unknown keys.
                    AppendLegacy(el.TryGetProperty("fallback", out var fb) && fb.ValueKind == JsonValueKind.String
                        ? fb.GetString() ?? string.Empty
                        : key.GetString() ?? string.Empty, style, into);
                if (el.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
                    foreach (var child in extra.EnumerateArray())
                        Walk(child, style, into);
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                AppendLegacy(el.ToString(), inherited, into);
                break;
        }
    }

    private static Style Apply(JsonElement el, Style parent)
    {
        var color = parent.Color;
        if (el.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String && ResolveColor(c.GetString()) is { } resolved)
            color = resolved;
        return new Style(
            color,
            Flag(el, "bold", parent.Bold),
            Flag(el, "italic", parent.Italic),
            Flag(el, "underlined", parent.Underlined),
            Flag(el, "strikethrough", parent.Strikethrough));
    }

    private static bool Flag(JsonElement el, string name, bool inherited)
        => el.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.True : inherited;

    private static string? ResolveColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value[0] == '#' && value.Length == 7) return value.ToUpperInvariant();
        return NamedColors.TryGetValue(value, out var hex) ? hex : null;
    }

    /// <summary>Split a string on § codes. <paramref name="baseStyle"/> is what §r returns to.</summary>
    private static void AppendLegacy(string text, Style baseStyle, List<McTextSpan> into)
    {
        var style = baseStyle;
        var run = new StringBuilder();

        void Flush()
        {
            if (run.Length == 0) return;
            into.Add(new McTextSpan(run.ToString(), style.Color, style.Bold, style.Italic, style.Underlined, style.Strikethrough));
            run.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '§' || i + 1 >= text.Length)
            {
                run.Append(text[i]);
                continue;
            }

            var code = char.ToLowerInvariant(text[++i]);
            var next = style;
            if (LegacyColors.TryGetValue(code, out var hex))
                next = new Style(hex, false, false, false, false);
            else if (code == 'l') next = style with { Bold = true };
            else if (code == 'o') next = style with { Italic = true };
            else if (code == 'n') next = style with { Underlined = true };
            else if (code == 'm') next = style with { Strikethrough = true };
            else if (code == 'r') next = baseStyle;
            // 'k' (obfuscated) and unknown codes: no visible change.

            if (next != style)
            {
                Flush();
                style = next;
            }
        }
        Flush();
    }

    private static IReadOnlyList<McTextSpan> Merge(List<McTextSpan> spans)
    {
        var merged = new List<McTextSpan>(spans.Count);
        foreach (var span in spans)
        {
            if (span.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1] is var last && last with { Text = span.Text } == span)
                merged[^1] = last with { Text = last.Text + span.Text };
            else
                merged.Add(span);
        }
        return merged;
    }
}
