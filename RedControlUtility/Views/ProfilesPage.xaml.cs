using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RedControlUtility.Models;
using RedControlUtility.Services;
using static RedControlUtility.UiHelper;

namespace RedControlUtility.Views;

public sealed partial class ProfilesPage : Page
{
    private bool _loading = true;

    public ProfilesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Localization.LanguageChanged += Load;
        Load();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.Localization.LanguageChanged -= Load;
        App.SaveCurrentProfile();
    }

    private static IntPtr Hwnd => App.MainWindowInstance != null ? WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance) : IntPtr.Zero;

    private void Load()
    {
        _loading = true;
        var p = App.CurrentProfile;
        NameBox.Text = p.ProfileName;
        SummaryText.Text = L("Profiles.Summary", p.GpuModel, p.Architecture, p.Apps.Count);
        _loading = false;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        App.CurrentProfile.ProfileName = NameBox.Text;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var p = App.CurrentProfile;
        var report = await Task.Run(() => App.ApplyProfile(p));
        ShowReport(StatusBar, report);
    }

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        await Task.Run(() => App.CaptureProfile());
        App.SaveCurrentProfile();
        Load();
        Show(StatusBar, InfoBarSeverity.Success, L("Profiles.Captured"));
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = NativeFileDialog.ShowSaveDialog(Hwnd, $"{L("Profiles.FileFilter")}\0*.json\0", "json", $"{App.CurrentProfile.ProfileName}.json", L("Profiles.Export"));
            if (string.IsNullOrWhiteSpace(path)) return;
            await App.Profiles.SaveProfileAsync(path, App.CurrentProfile);
            Show(StatusBar, InfoBarSeverity.Success, L("Profiles.Exported"), path);
        }
        catch (Exception ex)
        {
            LogService.Error($"Exportar perfil: {ex.Message}");
            Show(StatusBar, InfoBarSeverity.Error, L("Profiles.Failed"), ex.Message);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = NativeFileDialog.ShowOpenDialog(Hwnd, $"{L("Profiles.FileFilter")}\0*.json\0", L("Profiles.Import"));
            if (string.IsNullOrWhiteSpace(path)) return;
            var loaded = await App.Profiles.LoadProfileAsync(path);
            if (loaded == null)
            {
                Show(StatusBar, InfoBarSeverity.Error, L("Profiles.Invalid"));
                return;
            }

            foreach (var imported in loaded.Apps)
            {
                imported.RunOnStart = string.Empty;
                imported.RunOnExit = string.Empty;
            }

            App.CurrentProfile = loaded;
            App.SaveCurrentProfile();
            App.RestartEngine();
            Load();
            Show(StatusBar, InfoBarSeverity.Success, L("Profiles.Imported"), L("Profiles.ImportedHint"));
        }
        catch (Exception ex)
        {
            LogService.Error($"Importar perfil: {ex.Message}");
            Show(StatusBar, InfoBarSeverity.Error, L("Profiles.Failed"), ex.Message);
        }
    }

    private void ResetColor_Click(object sender, RoutedEventArgs e)
    {
        var p = App.CurrentProfile;
        p.ColorProfile = "Default";
        p.CustomGamma = p.CustomRed = p.CustomGreen = p.CustomBlue = 1.0;
        bool ok = App.DisplayColor.ApplyColorProfile("Default");
        Show(StatusBar, ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning, L(ok ? "Profiles.ColorReset" : "Display.GammaFailed"));
    }
}
