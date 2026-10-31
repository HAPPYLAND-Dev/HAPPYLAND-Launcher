using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;

/// <summary>
/// Raised by <see cref="IInstanceImporter"/> implementations when the supplied zip is not a
/// valid Hyperion instance archive (missing manifest, malformed JSON, unsupported FormatVersion).
/// The message is intentionally friendly so it can be surfaced to the user verbatim from the
/// "Import from zip..." button.
/// </summary>
public sealed class InstanceImportException : Exception
{
    /// <summary>Default ctor for a freshly-thrown error with a user-facing message.</summary>
    public InstanceImportException(string message) : base(message) { }

    /// <summary>Wrap an inner failure (zip decoder, JSON parser, etc.) with a friendly message.</summary>
    public InstanceImportException(string message, Exception inner) : base(message, inner) { }
}
