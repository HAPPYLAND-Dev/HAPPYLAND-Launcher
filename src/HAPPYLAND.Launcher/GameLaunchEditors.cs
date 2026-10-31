using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace Happyland.Launcher;

public sealed class MemoryEditor : UserControl
{
    private const int MinimumMemory = 512;
    private readonly Slider slider;
    private readonly NumericUpDown input;
    private int value;
    private bool syncing;

    public MemoryEditor()
    {
        MinWidth = 0;
        var availableMemory = (int)Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024), MinimumMemory, int.MaxValue);
        slider = new Slider
        {
            Maximum = availableMemory, Minimum = MinimumMemory, TickFrequency = 256,
            SmallChange = 256, LargeChange = 1024, IsSnapToTickEnabled = true,
            Height = 32, MinHeight = 0, MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center
        };
        input = new NumericUpDown
        {
            Maximum = availableMemory, Minimum = MinimumMemory, Increment = 256,
            ShowButtonSpinner = false, FormatString = "0", ParsingNumberStyle = NumberStyles.Integer,
            Height = 32, MinHeight = 0, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(slider, "游戏内存");
        AutomationProperties.SetName(input, "游戏内存 MB");
        var unit = new TextBlock { Text = "MB", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, Classes = { "small", "muted" } };
        var row = new Grid { MinWidth = 0, ColumnDefinitions = new ColumnDefinitions("*,8,64,6,Auto"), Children = { slider, input, unit } };
        Grid.SetColumn(input, 2); Grid.SetColumn(unit, 4);
        Content = row;

        input.ValueChanged += (_, _) =>
        {
            if (syncing || input.Value is not decimal amount) return;
            value = (int)amount;
            syncing = true;
            try { slider.Value = value; }
            finally { syncing = false; }
        };
        slider.PropertyChanged += (_, change) =>
        {
            if (syncing || change.Property != RangeBase.ValueProperty) return;
            value = (int)Math.Round(slider.Value, MidpointRounding.AwayFromZero);
            syncing = true;
            try { input.Value = value; }
            finally { syncing = false; }
        };
        input.LostFocus += (_, _) =>
        {
            if (input.Value is not null) return;
            syncing = true;
            try { input.Value = value; }
            finally { syncing = false; }
        };
        Value = Math.Min(4096, availableMemory);
    }

    public int Value
    {
        get => value;
        set
        {
            this.value = Math.Max(MinimumMemory, value);
            syncing = true;
            try
            {
                input.Maximum = Math.Max(input.Maximum, this.value);
                slider.Maximum = Math.Max(slider.Maximum, this.value);
                input.Value = this.value;
                slider.Value = this.value;
            }
            finally { syncing = false; }
        }
    }
}

public sealed class ResolutionEditor : UserControl
{
    private static readonly (int Width, int Height)[] Presets = [(1280, 720), (1600, 900), (1920, 1080), (2560, 1440)];
    private readonly ComboBox presets;
    private readonly NumericUpDown widthInput = Number(320, "宽");
    private readonly NumericUpDown heightInput = Number(240, "高");
    private int width = 1280, height = 720;
    private bool syncing;

    public ResolutionEditor()
    {
        MinWidth = 0;
        presets = new ComboBox
        {
            ItemsSource = Presets.Select(item => $"{item.Width} × {item.Height}").Append("自定义").ToArray(),
            Height = 32, MinHeight = 0, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<string>((name, _) => new TextBlock
            {
                Text = name, MinWidth = 0, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            })
        };
        AutomationProperties.SetName(presets, "窗口大小预设");
        AutomationProperties.SetName(widthInput, "窗口宽度");
        AutomationProperties.SetName(heightInput, "窗口高度");
        var separator = new TextBlock { Text = "×", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, Classes = { "small", "muted" } };
        var row = new Grid { MinWidth = 0, ColumnDefinitions = new ColumnDefinitions("*,6,52,12,52"), Children = { presets, widthInput, separator, heightInput } };
        Grid.SetColumn(widthInput, 2); Grid.SetColumn(separator, 3); Grid.SetColumn(heightInput, 4);
        Content = row;

        presets.SelectionChanged += (_, _) =>
        {
            if (syncing || presets.SelectedIndex < 0 || presets.SelectedIndex >= Presets.Length) return;
            var selected = Presets[presets.SelectedIndex];
            ApplyResolution(selected.Width, selected.Height);
        };
        widthInput.ValueChanged += (_, _) =>
        {
            if (syncing || widthInput.Value is not decimal amount) return;
            width = (int)amount;
            SelectCustom();
        };
        heightInput.ValueChanged += (_, _) =>
        {
            if (syncing || heightInput.Value is not decimal amount) return;
            height = (int)amount;
            SelectCustom();
        };
        widthInput.LostFocus += (_, _) => RestoreEmptyInput(widthInput, width);
        heightInput.LostFocus += (_, _) => RestoreEmptyInput(heightInput, height);
        ApplyResolution(width, height);
    }

    public int WidthValue
    {
        get => width;
        set => ApplyResolution(Math.Clamp(value, 320, 16384), height);
    }

    public int HeightValue
    {
        get => height;
        set => ApplyResolution(width, Math.Clamp(value, 240, 16384));
    }

    private void ApplyResolution(int width, int height)
    {
        this.width = width;
        this.height = height;
        syncing = true;
        try
        {
            widthInput.Value = width;
            heightInput.Value = height;
            var match = Array.FindIndex(Presets, item => item.Width == width && item.Height == height);
            presets.SelectedIndex = match >= 0 ? match : Presets.Length;
        }
        finally { syncing = false; }
    }

    private void SelectCustom()
    {
        syncing = true;
        try { presets.SelectedIndex = Presets.Length; }
        finally { syncing = false; }
    }

    private void RestoreEmptyInput(NumericUpDown input, int previousValue)
    {
        if (input.Value is not null) return;
        syncing = true;
        try { input.Value = previousValue; }
        finally { syncing = false; }
    }

    private static NumericUpDown Number(int minimum, string watermark) => new()
    {
        Minimum = minimum, Maximum = 16384, Increment = 1, ShowButtonSpinner = false,
        FormatString = "0", ParsingNumberStyle = NumberStyles.Integer,
        Watermark = watermark, Height = 32, MinHeight = 0, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch
    };
}
