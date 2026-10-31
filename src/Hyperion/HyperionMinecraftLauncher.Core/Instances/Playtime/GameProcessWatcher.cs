using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Playtime;

/// <summary>Thin OS-process helpers for play-time tracking of a game launched by pid.</summary>
public static class GameProcessWatcher
{
    /// <summary>
    /// True when <paramref name="processId"/> is alive and started within <paramref name="tolerance"/>
    /// of <paramref name="expectedStart"/>. The start-time check stops a pid reused after a reboot
    /// from being mistaken for the journaled game.
    /// </summary>
    public static bool IsRunning(int processId, DateTimeOffset expectedStart, TimeSpan tolerance)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return false;
            var started = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            return (started - expectedStart).Duration() <= tolerance;
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch (InvalidOperationException)
        {
            return false; // exited between the lookup and the property reads
        }
        catch (Win32Exception)
        {
            return false; // start time not readable: can't prove it is ours
        }
    }

    /// <summary>Completes when the process exits; immediately when it is already gone.</summary>
    public static async Task WaitForExitAsync(int processId, CancellationToken cancellationToken)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Already exited / not associated with a live process any more.
            }
        }
    }
}
