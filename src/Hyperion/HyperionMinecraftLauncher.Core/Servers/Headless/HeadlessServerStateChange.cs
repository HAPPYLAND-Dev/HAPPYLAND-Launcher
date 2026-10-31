namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Diff payload fired by <see cref="IHeadlessServerOrchestrator.StateChanged"/> when one of
/// the tracked servers transitions between states. The UI listens for this to redraw the
/// per-server status label and refresh Start/Stop command CanExecute results.
/// </summary>
/// <param name="ServerId"><see cref="HeadlessServer.Id"/> of the affected server.</param>
/// <param name="OldState">Previous state.</param>
/// <param name="NewState">New state.</param>
/// <param name="ProcessId">OS pid when <paramref name="NewState"/> is <see cref="HeadlessServerState.Running"/>; otherwise <c>0</c>.</param>
public sealed record HeadlessServerStateChange(
    string ServerId,
    HeadlessServerState OldState,
    HeadlessServerState NewState,
    int ProcessId);
