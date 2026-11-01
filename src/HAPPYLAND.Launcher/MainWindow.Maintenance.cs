using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Happyland.Launcher.Core;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace Happyland.Launcher;

public sealed partial class MainWindow
{
    private async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Cancel() => result.TrySetResult(false);
        var cancel = new Button { Content = "取消", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var confirm = new Button { Content = confirmText, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "primary" } };
        cancel.Click += (_, _) => Cancel();
        confirm.Click += (_, _) => result.TrySetResult(true);
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*"), Children = { cancel, confirm } };
        Grid.SetColumn(confirm, 2);
        var content = new StackPanel { Margin = new Thickness(16), Spacing = 16, Children =
        { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons } };
        return await ShowOverlayAsync(content, title, Cancel, result.Task, () => cancel.Focus());
    }

    private async Task UninstallGameAsync()
    {
        if (!await ConfirmAsync("卸载游戏", "将移除游戏本体和 Java。存档、Mod 和游戏设置会保留。", "卸载")) return;
        toast.BeginProgress("正在卸载游戏");
        try { await game.UninstallAsync(lifetime.Token); }
        finally { await SetActionAsync(); }
        SetStatus("游戏已卸载");
    }

    private async Task ReinstallJavaAsync()
    {
        installed ??= await packs.GetInstalledAsync(lifetime.Token);
        if (installed is null) throw new UserActionException("请先下载游戏。");
        if (!await ConfirmAsync("重装运行库", "将重新下载并安装游戏所需的 Java。", "重装")) return;
        toast.BeginProgress("正在重装运行库");
        var progress = new Progress<LaunchProgress>(item => toast.ShowProgress(item.Fraction is double value ? $"正在重装运行库 {value:P0}" : "正在重装运行库", item.Fraction));
        try { await game.ReinstallJavaAsync(installed, progress, lifetime.Token); }
        finally { await SetActionAsync(); }
        SetStatus("运行库已重装");
    }
}
