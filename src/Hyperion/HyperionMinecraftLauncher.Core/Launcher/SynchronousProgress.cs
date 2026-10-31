using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// <see cref="IProgress{T}"/> implementation that invokes the callback inline on the
/// reporting thread - unlike <see cref="System.Progress{T}"/> which marshals to the captured
/// <see cref="System.Threading.SynchronizationContext"/>. Used by the view-model and any caller
/// that needs deterministic in-order delivery (tests, batch CLIs, etc.). Avalonia's binding
/// system re-dispatches <see cref="System.ComponentModel.INotifyPropertyChanged"/> notifications
/// to the UI thread on its own, so no marshalling is needed here.
/// </summary>
public sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _onReport;

    public SynchronousProgress(Action<T> onReport)
    {
        _onReport = onReport ?? throw new ArgumentNullException(nameof(onReport));
    }

    public void Report(T value) => _onReport(value);
}
