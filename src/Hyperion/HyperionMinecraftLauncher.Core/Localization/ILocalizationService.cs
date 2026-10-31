using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Localization;

/// <summary>
/// Localization service contract. Looks up UI strings by key, allows the active culture to be
/// switched at runtime, and notifies subscribers when the culture changes.
/// </summary>
/// <remarks>
/// The service is intentionally framework-agnostic so it can be wired from the Avalonia App
/// layer, a CLI, or unit tests. Implementations are expected to expose every key currently in
/// the canonical English resource file and fall back to the English value when a translation is
/// missing for a non-English culture, never throwing for an unknown culture or key.
/// </remarks>
public interface ILocalizationService
{
    /// <summary>Look up a localized string by key. Returns the key itself if no entry exists.</summary>
    /// <param name="key">UpperCamelCase dotted key, e.g. <c>MainWindow.Title</c>.</param>
    string this[string key] { get; }

    /// <summary>
    /// Look up a localized string by key and format it with <see cref="string.Format(string, object[])"/>.
    /// Returns the key itself if no entry exists.
    /// </summary>
    /// <param name="key">UpperCamelCase dotted key, e.g. <c>Logs.LineCountFooter</c>.</param>
    /// <param name="args">Positional format arguments (<c>{0}</c>, <c>{1}</c>, ...).</param>
    string Get(string key, params object[] args);

    /// <summary>The currently active culture name (BCP 47, e.g. <c>en</c>, <c>ru</c>, <c>fr-CA</c>).</summary>
    string CurrentCulture { get; }

    /// <summary>
    /// Switch the active culture. Implementations fire <see cref="LanguageChanged"/> exactly once
    /// per real switch (no event when the new culture equals the current one).
    /// </summary>
    Task SetCultureAsync(string culture, CancellationToken cancellationToken);

    /// <summary>
    /// Fired after the active culture changes. UI bindings should listen and re-evaluate their
    /// indexer reads when this fires (or rely on <see cref="System.ComponentModel.INotifyPropertyChanged"/>
    /// on the service for X:Bind / compiled bindings).
    /// </summary>
    event EventHandler? LanguageChanged;

    /// <summary>
    /// All cultures the launcher ships translations for, including <c>en</c>. Returned in the
    /// order presented in the settings dropdown (alphabetical by display name is fine for now).
    /// </summary>
    IReadOnlyList<string> AvailableCultures { get; }
}
