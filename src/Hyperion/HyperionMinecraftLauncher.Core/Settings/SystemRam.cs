using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;

/// <summary>Best-effort total physical RAM probe, used to clamp the memory slider on the Settings page.</summary>
public static class SystemRam
{
    // T18: total RAM and the derived recommended heap cap are constant for the lifetime of the
    // process, so we compute them lazily once. The MainViewModel ctor calls
    // RecommendedMaxHeapMb() today; if future call sites appear (e.g. headless server tooling)
    // they reuse the cached value at zero P/Invoke cost.
    private static readonly Lazy<int> _totalMb = new(ComputeTotalMb, LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<int> _recommendedMaxHeapMb = new(ComputeRecommendedMaxHeapMb, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>Return total system RAM in MiB. Falls back to 8192 (8 GB) when the OS doesn't expose a known counter.</summary>
    public static int TotalMb() => _totalMb.Value;

    /// <summary>Recommended maximum heap in MiB for a 64-bit Minecraft JVM on this machine. We leave 25% headroom for the OS.</summary>
    public static int RecommendedMaxHeapMb() => _recommendedMaxHeapMb.Value;

    private static int ComputeTotalMb()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (TryGetWindowsRamMb(out var mb))
                    return mb;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (TryGetLinuxRamMb(out var mb))
                    return mb;
            }
        }
        catch
        {
            // ignore - we always have the constant fallback below
        }
        return 8192;
    }

    private static int ComputeRecommendedMaxHeapMb()
    {
        var ram = _totalMb.Value;
        // Reserve a quarter for the OS / browser / other processes.
        var max = (int)(ram * 0.75);
        // Never recommend more than 16 GB - modern MC barely uses 6 GB at peak.
        return Math.Min(max, 16384);
    }

    private static bool TryGetWindowsRamMb(out int mb)
    {
        // Avoid pulling System.Management; use the well-known GlobalMemoryStatusEx P/Invoke.
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref status))
        {
            mb = (int)(status.ullTotalPhys / (1024UL * 1024UL));
            return mb > 0;
        }
        mb = 0;
        return false;
    }

    private static bool TryGetLinuxRamMb(out int mb)
    {
        mb = 0;
        const string meminfo = "/proc/meminfo";
        if (!File.Exists(meminfo)) return false;
        foreach (var line in File.ReadLines(meminfo))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                // "MemTotal:       16384000 kB"
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                {
                    mb = (int)(kb / 1024);
                    return mb > 0;
                }
                break;
            }
        }
        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
