using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace Happyland.Launcher.Core;

public enum GameStartupStatus { Ready, Exited, TimedOut }

public sealed record GameLaunchResult(int ProcessId, GameStartupSignal Startup);

public sealed class GameStartupSignal : IObserver<GameLogLine>
{
    private readonly TaskCompletionSource<GameStartupStatus> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<GameStartupStatus> WaitAsync(CancellationToken ct)
    {
        try { return await completion.Task.WaitAsync(TimeSpan.FromMinutes(2), ct); }
        catch (TimeoutException) { return GameStartupStatus.TimedOut; }
    }

    public void OnNext(GameLogLine line)
    {
        if (completion.Task.IsCompleted) return;
        var text = line.Text;
        // SDL reports window creation directly; older clients report their rendered texture atlas.
        if (text.Contains("Created window using SDL video driver:", StringComparison.Ordinal) ||
            text.Contains("Created:", StringComparison.Ordinal) && text.Contains("-atlas", StringComparison.Ordinal))
            completion.TrySetResult(GameStartupStatus.Ready);
    }

    public void OnCompleted() => completion.TrySetResult(GameStartupStatus.Exited);
    public void OnError(Exception error) => completion.TrySetException(error);
}
