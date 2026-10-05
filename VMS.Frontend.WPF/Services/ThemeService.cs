using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.Frontend.WPF.Services;

/// <summary>
/// Light/Dark switching via swappable ResourceDictionary — same technique as
/// <see cref="LocalizationService"/>. Brush/Style resources swap instantly for every element that
/// resolves them through a Style (implicit TargetType styles, and any Style itself fetched via
/// DynamicResource), which covers ordinary controls (TextBox, DataGrid, Calendar, plain buttons).
///
/// It does NOT retroactively restyle a Button whose own inline `Button.Style` uses
/// `BasedOn="{StaticResource ...}"` (WPF requires BasedOn to be Static — that base Style object is
/// captured once, at the moment that particular Button was constructed) or a ComboBox/DatePicker's
/// custom ControlTemplate (also captured once). Recreating the current top-level window right
/// after the swap — see App.OnThemeChanged — is what actually makes those repaint: a freshly
/// parsed window resolves every StaticResource against whichever dictionary is active at that
/// moment. Already-open secondary windows (Archive, User Management, ...) keep whatever theme was
/// active when they were opened, and pick up the new one the next time they're opened — documented
/// in the READMEs' known-limitations section.
/// </summary>
public partial class ThemeService : ObservableObject
{
    private const string DarkThemeSource = "Themes/DarkTheme.xaml";
    private const string LightThemeSource = "Themes/LightTheme.xaml";

    [ObservableProperty]
    private bool _isDarkTheme = true;

    public event Action? ThemeChanged;

    public void SetTheme(bool isDark)
    {
        var appResources = Application.Current.Resources.MergedDictionaries;

        var uri = new Uri(isDark ? DarkThemeSource : LightThemeSource, UriKind.Relative);
        var newDictionary = new ResourceDictionary { Source = uri };

        var existing = appResources.FirstOrDefault(d =>
            d.Source is not null &&
            (d.Source.OriginalString == DarkThemeSource || d.Source.OriginalString == LightThemeSource));
        if (existing is not null)
        {
            appResources.Remove(existing);
        }

        appResources.Add(newDictionary);
        IsDarkTheme = isDark;
        ThemeChanged?.Invoke();
    }

    public void ToggleTheme() => SetTheme(!IsDarkTheme);
}
