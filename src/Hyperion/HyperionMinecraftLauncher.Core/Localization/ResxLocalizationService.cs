using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Localization;

/// <summary>
/// <see cref="ILocalizationService"/> implementation that wraps a <see cref="ResourceManager"/>
/// over a <c>Strings.resx</c> resource family. The English <c>Strings.resx</c> is the canonical
/// source; localized cultures land as <c>Strings.{culture}.resx</c> next to it.
/// </summary>
/// <remarks>
/// The service also implements <see cref="INotifyPropertyChanged"/> with a single virtual
/// "Item[]" property name. XAML bindings written as
/// <c>{Binding [SomeKey], Source={x:Static loc:LocalizationService.Default}}</c> pick up the
/// change automatically when <see cref="SetCultureAsync"/> fires the property-changed event.
/// </remarks>
public sealed class ResxLocalizationService : ILocalizationService, INotifyPropertyChanged
{
    private readonly ResourceManager _resourceManager;
    private readonly IReadOnlyList<string> _availableCultures;
    private CultureInfo _currentCulture;
    private readonly object _gate = new();

    /// <summary>
    /// Construct with a fully-prepared <see cref="ResourceManager"/> (typically pointed at a
    /// resx baked into the App assembly), the initial culture, and the cultures the launcher
    /// ships translations for. The first entry in <paramref name="availableCultures"/> should
    /// always be the canonical English culture (<c>en</c>).
    /// </summary>
    public ResxLocalizationService(
        ResourceManager resourceManager,
        string initialCulture,
        IReadOnlyList<string> availableCultures)
    {
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        if (availableCultures is null || availableCultures.Count == 0)
            throw new ArgumentException("At least one culture must be supplied.", nameof(availableCultures));
        _availableCultures = availableCultures;
        _currentCulture = ParseCultureOrFallback(initialCulture);
    }

    /// <inheritdoc />
    public string this[string key] => Lookup(key) ?? key;

    /// <inheritdoc />
    public string Get(string key, params object[] args)
    {
        var format = Lookup(key);
        if (format is null) return key;
        if (args is null || args.Length == 0) return format;
        return string.Format(CultureInfo.InvariantCulture, format, args);
    }

    /// <inheritdoc />
    public string CurrentCulture
    {
        get
        {
            lock (_gate) return _currentCulture.Name.Length == 0 ? "en" : _currentCulture.Name;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> AvailableCultures => _availableCultures;

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public Task SetCultureAsync(string culture, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parsed = ParseCultureOrFallback(culture);
        bool changed;
        lock (_gate)
        {
            changed = !string.Equals(parsed.Name, _currentCulture.Name, StringComparison.OrdinalIgnoreCase);
            if (changed) _currentCulture = parsed;
        }
        if (changed)
        {
            // Fire INotifyPropertyChanged with the indexer name first so XAML bindings of the form
            // {Binding [Key], Source=...} re-evaluate, then notify any imperative subscribers.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
        return Task.CompletedTask;
    }

    private string? Lookup(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        CultureInfo culture;
        lock (_gate) culture = _currentCulture;
        // ResourceManager falls back from "ru-RU" -> "ru" -> invariant (which is the English
        // resx file). That fallback is exactly the "translation missing -> English" behaviour
        // we want, so no extra plumbing needed here. Catch MissingManifestResourceException so
        // a malformed resource family never crashes the UI.
        try
        {
            return _resourceManager.GetString(key, culture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }

    private static CultureInfo ParseCultureOrFallback(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return CultureInfo.InvariantCulture;
        try
        {
            return CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
