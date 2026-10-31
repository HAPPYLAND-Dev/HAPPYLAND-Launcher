using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Updates;

/// <summary>
/// A newer launcher release the user can download. Returned by an <see cref="IUpdateChecker"/>
/// when (and only when) the remote feed reports a version strictly newer than the running build.
/// </summary>
/// <param name="LatestVersion">The X.Y.Z tag of the newer release, with the leading "v" stripped.</param>
/// <param name="ReleaseUrl">Web URL the launcher banner opens in the default browser.</param>
/// <param name="PublishedAt">UTC publish timestamp from the upstream feed.</param>
/// <param name="Notes">Release notes (markdown / plain text). May be empty when the feed omits a body.</param>
public sealed record UpdateInfo(
    string LatestVersion,
    string ReleaseUrl,
    DateTimeOffset PublishedAt,
    string Notes);
