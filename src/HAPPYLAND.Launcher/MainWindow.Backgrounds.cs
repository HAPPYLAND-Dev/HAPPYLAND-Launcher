using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Happyland.Launcher.Appearance;

namespace Happyland.Launcher;

public sealed partial class MainWindow
{
    private readonly StackPanel backgroundTiles = new() { Orientation = Orientation.Horizontal };
    private readonly List<Bitmap> backgroundThumbnails = [];
    private readonly List<CustomBackground> customBackgrounds = [];
    private readonly Dictionary<string, BackgroundAdjustment> backgroundAdjustments = new(StringComparer.Ordinal);
    private readonly Dictionary<Button, string> backgroundTileKeys = [];
    private bool applyingBackground;

    private sealed record CustomBackground(string Name, string FilePath);
    private sealed record BackgroundAdjustment(int Strength, int Shade);

    private string? CurrentBackgroundKey => preferences.BackgroundPresetId is { } id ? "preset:" + id
        : preferences.BackgroundFile is { } file ? CustomBackgroundKey(file) : null;
    private static string CustomBackgroundKey(string file) => "file:" + Path.GetFullPath(file);

    private void InitializeBackgrounds()
    {
        customBackgrounds.AddRange(preferences.CustomBackgrounds ?? []);
        foreach (var pair in preferences.BackgroundAdjustments ?? []) backgroundAdjustments[pair.Key] = pair.Value;
        if (preferences.BackgroundFile is { } file && !customBackgrounds.Any(item => item.FilePath == file))
            customBackgrounds.Add(new CustomBackground("自定义背景", file));
        if (preferences.BackgroundPresetId is null && preferences.BackgroundFile is not null && preferences.BackgroundAdjustments is null)
            backgroundAdjustments[CurrentBackgroundKey!] = new(preferences.BackgroundOpacity, preferences.BackgroundShade);
    }

    private static Bitmap LoadCustomBackground(string path, int width = 1600)
    {
        using var input = File.OpenRead(path);
        return Bitmap.DecodeToWidth(input, width, BitmapInterpolationMode.HighQuality);
    }

    private BackgroundAdjustment BackgroundValues(BackgroundPreset? preset, string? file)
    {
        var key = preset is null ? CustomBackgroundKey(file!) : "preset:" + preset.Id;
        return backgroundAdjustments.GetValueOrDefault(key) ?? new BackgroundAdjustment(preset?.Strength ?? 60, preset?.Shade ?? 30);
    }

    private void LoadBackground()
    {
        var preset = BackgroundPreset.All.FirstOrDefault(item => item.Id == preferences.BackgroundPresetId);
        if (preset is null && preferences.BackgroundFile is null) return;
        try
        {
            backgroundBitmap = preset is null ? LoadCustomBackground(preferences.BackgroundFile!) : preset.Load();
            var values = BackgroundValues(preset, preferences.BackgroundFile);
            preferences = preferences with { BackgroundOpacity = values.Strength, BackgroundShade = values.Shade };
            backgroundAdjustments[CurrentBackgroundKey!] = values;
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            preferences = preferences with { BackgroundFile = null, BackgroundPresetId = null };
            SetStatus("背景图片无法读取，请重新选择。", true, e.ToString());
        }
    }

    private Control BuildBackgroundGallery()
    {
        RebuildBackgroundTiles();
        var add = BackgroundTile(new PixelIcon { Kind = PixelIconKind.Plus, Width = 16, Height = 16 }, "添加背景");
        add.Margin = new Thickness(0, 0, 0, 8);
        add.VerticalAlignment = VerticalAlignment.Top;
        add.Click += async (_, _) => await ExecuteAsync(ChooseBackgroundAsync, "图片无法打开，请选择其他图片。");
        var scroll = new ScrollViewer
        {
            Content = backgroundTiles, Height = 68,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        var gallery = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,56"), Children = { scroll, add } };
        Grid.SetColumn(add, 2);
        return gallery;
    }

    private void RebuildBackgroundTiles()
    {
        foreach (var button in backgroundTiles.Children.OfType<Button>()) editingControls.Remove(button);
        backgroundTiles.Children.Clear();
        backgroundTileKeys.Clear();
        foreach (var bitmap in backgroundThumbnails) bitmap.Dispose();
        backgroundThumbnails.Clear();
        var none = BackgroundTile(new PixelIcon { Kind = PixelIconKind.Prohibited, Width = 16, Height = 16 }, "无背景");
        none.Click += async (_, _) => await ExecuteAsync(UseNoBackgroundAsync);
        backgroundTileKeys[none] = "";
        backgroundTiles.Children.Add(none);
        foreach (var preset in BackgroundPreset.All)
            AddBackgroundTile(preset.Name, "preset:" + preset.Id, () => preset.Load(160),
                () => SelectBackgroundAsync(preset, null), preset.Alignment);
        foreach (var custom in customBackgrounds)
            AddBackgroundTile(custom.Name, CustomBackgroundKey(custom.FilePath), () => LoadCustomBackground(custom.FilePath, 160),
                () => SelectBackgroundAsync(null, custom.FilePath));
        UpdateBackgroundSelection();
    }

    private Button BackgroundTile(Control content, string name)
    {
        var button = new Button
        {
            Width = 56, Height = 56, MinHeight = 0, Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(2),
            Content = content, Classes = { "background-tile" }
        };
        ToolTip.SetTip(button, name);
        ToolTip.SetPlacement(button, PlacementMode.Top);
        ToolTip.SetVerticalOffset(button, -4);
        AutomationProperties.SetName(button, name);
        editingControls.Add(button);
        return button;
    }

    private void AddBackgroundTile(string name, string key, Func<Bitmap> load, Func<Task> select, AlignmentY alignment = AlignmentY.Center)
    {
        Bitmap thumbnail;
        try { thumbnail = load(); }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            logger.Error("Could not load background thumbnail", e);
            return;
        }
        backgroundThumbnails.Add(thumbnail);
        var preview = new Border { Width = 52, Height = 52, Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill, AlignmentY = alignment } };
        RenderOptions.SetBitmapInterpolationMode(preview, BitmapInterpolationMode.HighQuality);
        var button = BackgroundTile(preview, name);
        button.Click += async (_, _) => await ExecuteAsync(select, "背景图片无法打开，请重试。");
        backgroundTileKeys[button] = key;
        backgroundTiles.Children.Add(button);
    }

    private void UpdateBackgroundSelection()
    {
        foreach (var pair in backgroundTileKeys) pair.Key.Classes.Set("selected", pair.Value == (CurrentBackgroundKey ?? ""));
    }

    private async Task SelectBackgroundAsync(BackgroundPreset? preset, string? file)
    {
        var bitmap = await Task.Run(() => preset is null ? LoadCustomBackground(file!) : preset.Load(), lifetime.Token);
        ActivateBackground(bitmap, preset, file);
        await SavePreferencesAsync();
    }

    private void ActivateBackground(Bitmap bitmap, BackgroundPreset? preset, string? file)
    {
        var values = BackgroundValues(preset, file);
        backgroundBrush.Source = null;
        backgroundBitmap?.Dispose();
        backgroundBitmap = bitmap;
        preferences = preferences with { BackgroundPresetId = preset?.Id, BackgroundFile = file };
        backgroundAdjustments[CurrentBackgroundKey!] = values;
        applyingBackground = true;
        try { backgroundOpacity.Value = values.Strength; backgroundShade.Value = values.Shade; }
        finally { applyingBackground = false; }
        ApplyAppearance();
        UpdateBackgroundSelection();
    }

    private async Task SaveBackgroundAdjustmentAsync()
    {
        if (applyingBackground || CurrentBackgroundKey is not { } key) return;
        backgroundAdjustments[key] = new((int)(backgroundOpacity.Value ?? 60), (int)(backgroundShade.Value ?? 30));
        await TrySavePreferencesAsync();
    }

    private async Task UseNoBackgroundAsync()
    {
        backgroundBrush.Source = null;
        backgroundBitmap?.Dispose();
        backgroundBitmap = null;
        preferences = preferences with { BackgroundPresetId = null, BackgroundFile = null };
        ApplyAppearance();
        UpdateBackgroundSelection();
        await SavePreferencesAsync();
    }

    private async Task ChooseBackgroundAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择背景图片", AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (files.Count == 0) return;
        await using var input = await files[0].OpenReadAsync();
        using var data = new MemoryStream();
        await input.CopyToAsync(data, lifetime.Token);
        var bytes = data.ToArray();
        var bitmap = await Task.Run(() =>
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return Bitmap.DecodeToWidth(stream, 1600, BitmapInterpolationMode.HighQuality);
        }, lifetime.Token);
        var path = Path.Combine(root, "appearance", "backgrounds", Guid.NewGuid().ToString("N") + ".image");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, lifetime.Token);
        }
        catch { bitmap.Dispose(); throw; }
        customBackgrounds.Add(new CustomBackground(files[0].Name, path));
        ActivateBackground(bitmap, null, path);
        RebuildBackgroundTiles();
        await SavePreferencesAsync();
    }
}
