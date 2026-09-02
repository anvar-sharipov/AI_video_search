using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.Frontend.WPF.Services;

/// <summary>
/// Live language switching via swappable ResourceDictionary (not .resx/CultureInfo — those
/// need either a restart or manually re-binding every element; DynamicResource updates the
/// whole visual tree the moment the merged dictionary changes).
///
/// CLAUDE.md mandates every user-facing string go through this: added to all three
/// Localization/Strings.*.xaml dictionaries and referenced via {DynamicResource} (XAML) or
/// Get(...) (C#) — never a literal in markup or code.
/// </summary>
public partial class LocalizationService : ObservableObject
{
    private const string DictionaryFolder = "Localization/Strings.";

    [ObservableProperty]
    private string _currentLanguage = "ru";

    public void SetLanguage(string languageCode)
    {
        var appResources = Application.Current.Resources.MergedDictionaries;

        var uri = new Uri($"{DictionaryFolder}{languageCode}.xaml", UriKind.Relative);
        var newDictionary = new ResourceDictionary { Source = uri };

        var existing = appResources.FirstOrDefault(d =>
            d.Source is not null && d.Source.OriginalString.StartsWith(DictionaryFolder, StringComparison.Ordinal));
        if (existing is not null)
        {
            appResources.Remove(existing);
        }

        appResources.Add(newDictionary);
        CurrentLanguage = languageCode;
    }

    /// <summary>For C# code that composes a message at a specific moment (StatusMessage, error text) rather than binding to it.</summary>
    public static string Get(string key, params object[] args)
    {
        var value = Application.Current.TryFindResource(key) as string ?? key;
        return args.Length == 0 ? value : string.Format(value, args);
    }
}
