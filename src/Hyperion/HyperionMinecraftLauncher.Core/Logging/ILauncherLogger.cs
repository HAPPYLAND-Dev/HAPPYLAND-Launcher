using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

/// <summary>
/// Minimal launcher-internal log sink. Production wiring writes to a daily-rotated
/// file under <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/logs</c>; tests can use
/// an in-memory implementation.
/// </summary>
public interface ILauncherLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
