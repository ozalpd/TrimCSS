using System.IO;
using System.Windows;

namespace TrimCSS.GUI.Services;

/// <summary>
/// Available UI color themes for the desktop application.
/// </summary>
public enum AppTheme
{
    Dark,
    Light
}

/// <summary>
/// Manages runtime theme dictionary swapping and user theme preference persistence.
/// </summary>
public static class ThemeService
{
    private const string DarkThemeSource = "Themes/DarkTheme.xaml";
    private const string LightThemeSource = "Themes/LightTheme.xaml";

    private static readonly string PreferenceFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TrimCSS",
        "theme.txt");

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    /// <summary>
    /// Applies the requested theme dictionary to the application's merged resources.
    /// </summary>
    public static void ApplyTheme(AppTheme theme, bool persist = true)
    {
        var app = Application.Current;
        if (app is not null)
        {
            string sourcePath = theme == AppTheme.Light ? LightThemeSource : DarkThemeSource;
            var mergedDictionaries = app.Resources.MergedDictionaries;

            var existingThemeDictionaries = mergedDictionaries
                .Where(d => d.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            foreach (var dict in existingThemeDictionaries)
            {
                mergedDictionaries.Remove(dict);
            }

            mergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(sourcePath, UriKind.Relative)
            });
        }

        CurrentTheme = theme;

        if (persist)
        {
            try
            {
                string? directory = Path.GetDirectoryName(PreferenceFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(PreferenceFilePath, theme.ToString());
            }
            catch
            {
                // Best-effort persistence; ignore I/O errors.
            }
        }
    }

    /// <summary>
    /// Toggles between Dark and Light themes and persists the choice.
    /// </summary>
    public static AppTheme ToggleTheme()
    {
        AppTheme nextTheme = CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ApplyTheme(nextTheme, persist: true);
        return nextTheme;
    }

    /// <summary>
    /// Loads any saved theme preference on application startup.
    /// </summary>
    public static void LoadSavedTheme()
    {
        try
        {
            if (File.Exists(PreferenceFilePath))
            {
                string saved = File.ReadAllText(PreferenceFilePath).Trim();
                if (Enum.TryParse<AppTheme>(saved, ignoreCase: true, out var theme) && theme == AppTheme.Light)
                {
                    ApplyTheme(AppTheme.Light, persist: false);
                }
            }
        }
        catch
        {
            // Fall back to default Dark theme on read errors.
        }
    }
}
