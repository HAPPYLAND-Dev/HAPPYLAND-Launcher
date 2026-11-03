using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Happyland.Contracts;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

namespace Happyland.Launcher;

public sealed partial class MainWindow
{
    private CancellationTokenSource? microsoftLoginCancellation;
    private MicrosoftLoginView? microsoftLoginView;
    private Task<bool>? microsoftLoginOverlay;

    private void ShowMicrosoftLogin(MicrosoftDeviceCodeInfo info)
    {
        if (microsoftLoginCancellation is null || microsoftLoginCancellation.IsCancellationRequested) return;
        microsoftLoginView?.ShowCode(info);
    }

    private MicrosoftLoginView CreateMicrosoftLogin() => new(async code =>
        {
            try { await Clipboard!.SetTextAsync(code); toast.Show("验证码已复制"); }
            catch (Exception e) { logger.Error("Could not copy the verification code", e); SetStatus("复制失败，请重试。", true, e.ToString()); }
        }, url =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
            catch (Exception e) { logger.Error("Could not open the Microsoft login page", e); SetStatus("登录页面无法打开。", true, e.ToString()); }
        });

    private async Task RemoveAccountAsync()
    {
        if (accounts.SelectedItem is not LauncherAccount account) return;
        if (!await ConfirmAsync("删除账号", $"确定删除账号“{account.Name}”？", "删除")) return;

        if (account.MicrosoftId is not null)
            await microsoft!.SignOutAsync(account.MicrosoftId, lifetime.Token);
        else
        {
            if (account.Uuid is not null)
                await skinCredentials.DeleteAsync(account.SkinServer!.Url, account.Login!, account.Uuid, lifetime.Token);
            SaveSkinAccounts(excluding: account);
        }

        accountList.Remove(account);
        var skin = account.Skin;
        account.Skin = null;
        skin?.Dispose();
        UpdateAccounts();
        await SavePreferencesAsync();
        SetStatus("账号已删除");
    }

    private async Task OpenSkinSiteAsync(SkinServer site)
    {
        var serverUri = new Uri(site.Url);
        var homepage = serverUri.GetLeftPart(UriPartial.Authority);
        try
        {
            var metadata = await skinHttp.GetFromJsonAsync<JsonElement>(serverUri, lifetime.Token);
            if (metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object &&
                meta.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object &&
                links.TryGetProperty("homepage", out var value) && value.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                homepage = uri.AbsoluteUri;
        }
        catch (Exception e) when (!lifetime.IsCancellationRequested && e is (HttpRequestException or JsonException or TaskCanceledException))
        {
            logger.Warn("Could not read the skin site's homepage; opening its domain.");
        }
        Process.Start(new ProcessStartInfo(homepage) { UseShellExecute = true });
    }
}
