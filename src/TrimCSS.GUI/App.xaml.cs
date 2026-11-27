using System.Windows;
using TrimCSS.GUI.Services;

namespace TrimCSS.GUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.LoadSavedTheme();
    }
}
