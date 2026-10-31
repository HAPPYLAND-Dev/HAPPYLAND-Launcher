using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>Base exception type for the launcher service. All friendly error paths derive from this.</summary>
public abstract class LauncherException : Exception
{
    protected LauncherException(string message) : base(message) { }
    protected LauncherException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The requested Minecraft version was not in the manifest.</summary>
public sealed class VersionNotFoundException : LauncherException
{
    public string VersionName { get; }

    public VersionNotFoundException(string versionName)
        : base($"Minecraft version '{versionName}' was not found in the manifest.")
    {
        VersionName = versionName;
    }

    public VersionNotFoundException(string versionName, Exception inner)
        : base($"Minecraft version '{versionName}' was not found in the manifest.", inner)
    {
        VersionName = versionName;
    }
}

/// <summary>The install pipeline failed (download / extract / java provisioning).</summary>
public sealed class InstallationFailedException : LauncherException
{
    public InstallationFailedException(string message) : base(message) { }
    public InstallationFailedException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The Java process could not be started (missing JRE, unsupported OS, etc.).</summary>
public sealed class GameProcessStartException : LauncherException
{
    public GameProcessStartException(string message) : base(message) { }
    public GameProcessStartException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Authentication failed (network, bad credentials, unsupported mode).</summary>
public sealed class AuthenticationFailedException : LauncherException
{
    public AuthenticationFailedException(string message) : base(message) { }
    public AuthenticationFailedException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The Mojang skin/cape API rejected the call (HTTP error, bad body, network failure).</summary>
public sealed class SkinUploadFailedException : LauncherException
{
    public SkinUploadFailedException(string message) : base(message) { }
    public SkinUploadFailedException(string message, Exception inner) : base(message, inner) { }
}
