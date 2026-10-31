using System.Globalization;

namespace Happyland.Launcher.Core;

public sealed record GamePreferences
{
    public int MaximumMemoryMb { get; init; } = 4096;
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool FullScreen { get; init; }
    public int RenderDistance { get; init; } = 12;
    public int MaximumFps { get; init; } = 120;
    public bool VSync { get; init; } = true;
    public int MasterVolume { get; init; } = 80;
    public int MusicVolume { get; init; } = 50;
    public int Brightness { get; init; } = 50;
    public int GuiScale { get; init; }

    public async Task ApplyAsync(string gameDirectory, CancellationToken ct)
    {
        var path = Path.Combine(gameDirectory, "options.txt");
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fullscreen"] = FullScreen ? "true" : "false",
            ["renderDistance"] = RenderDistance.ToString(CultureInfo.InvariantCulture),
            ["maxFps"] = MaximumFps.ToString(CultureInfo.InvariantCulture),
            ["enableVsync"] = VSync ? "true" : "false",
            ["soundCategory_master"] = (MasterVolume / 100d).ToString(CultureInfo.InvariantCulture),
            ["soundCategory_music"] = (MusicVolume / 100d).ToString(CultureInfo.InvariantCulture),
            ["gamma"] = (Brightness / 100d).ToString(CultureInfo.InvariantCulture),
            ["guiScale"] = GuiScale.ToString(CultureInfo.InvariantCulture)
        };
        var lines = File.Exists(path) ? (await File.ReadAllLinesAsync(path, ct)).ToList() : [];
        for (var i = 0; i < lines.Count; i++)
        {
            var separator = lines[i].IndexOf(':');
            if (separator < 0) continue;
            var key = lines[i][..separator];
            if (values.Remove(key, out var value)) lines[i] = key + ":" + value;
        }
        lines.AddRange(values.Select(p => p.Key + ":" + p.Value));
        Directory.CreateDirectory(gameDirectory);
        var staged = path + ".happyland.tmp";
        await File.WriteAllLinesAsync(staged, lines, ct);
        File.Move(staged, path, overwrite: true);
    }
}
