using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RedControlUtility.Models;
using RedControlUtility.Services;
using RedControlUtility.Services.Adlx;
using static RedControlUtility.UiHelper;

namespace RedControlUtility.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;
    private bool _checkingUpdate;
    private static readonly int[] Intervals = { 500, 1000, 2000, 5000 };

    public SettingsPage()
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
        App.Settings.Save();
    }

    private static AppSettings S => App.Settings.Current;

    private void Load()
    {
        _loading = true;
        try
        {
            if (LanguageCombo.Items.Count == 0)
            {
                foreach (var (code, name) in LocalizationService.Supported)
                    LanguageCombo.Items.Add(Item(name, code));
            }

            LanguageCombo.SelectedItem = LanguageCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, App.Localization.Language));

            ThemeCombo.Items.Clear();
            ThemeCombo.Items.Add(Item(L("Theme.System"), AppTheme.System));
            ThemeCombo.Items.Add(Item(L("Theme.Dark"), AppTheme.Dark));
            ThemeCombo.Items.Add(Item(L("Theme.Light"), AppTheme.Light));
            ThemeCombo.Items.Add(Item(L("Theme.Dracula"), AppTheme.Dracula));
            ThemeCombo.Items.Add(Item(L("Theme.CyberGreen"), AppTheme.CyberGreen));
            ThemeCombo.Items.Add(Item(L("Theme.CrimsonCarbon"), AppTheme.CrimsonCarbon));
            ThemeCombo.Items.Add(Item(L("Theme.ArcCobalt"), AppTheme.ArcCobalt));
            ThemeCombo.SelectedItem = ThemeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, (AppTheme)S.Theme));

            ApplyModeCombo.Items.Clear();
            ApplyModeCombo.Items.Add(Item(L("Settings.ModeInstant"), ApplyMode.Instant));
            ApplyModeCombo.Items.Add(Item(L("Settings.ModeManual"), ApplyMode.Manual));
            ApplyModeCombo.SelectedIndex = S.GraphicsApplyMode == ApplyMode.Instant ? 0 : 1;

            AdvancedToggle.IsOn = S.AdvancedMode;
            AutoRevertToggle.IsOn = S.AutoRevertTuning;
            LiveUndervoltToggle.IsOn = S.LiveApplyUndervolt;
            MinimizeToTrayToggle.IsOn = S.MinimizeToTray;
            PerAppToggle.IsOn = S.PerAppProfilesEnabled;
            AutoResetToggle.IsOn = S.AutoResetOnInstability;
            OverheatToggle.IsOn = S.OverheatWarn;
            OverheatSlider.Value = Math.Clamp(S.OverheatTempC, 50, 110);
            OverheatSecondsSlider.Value = Math.Clamp(S.OverheatSeconds, 3, 60);
            OverheatAlwaysToggle.IsOn = S.OverheatAlwaysOn;
            RefreshOverheatUi();
            NotifyToggle.IsOn = S.NotifyOnProfile;
            OverlaySecondsBox.Value = Math.Clamp(S.OverlaySeconds, 1, 30);
            OverlayCornerCombo.Items.Clear();
            string[] corners = { "Corner.TopLeft", "Corner.TopRight", "Corner.BottomLeft", "Corner.BottomRight", "Corner.TopCenter" };
            for (int i = 0; i < corners.Length; i++) OverlayCornerCombo.Items.Add(Item(L(corners[i]), i));
            OverlayCornerCombo.SelectedIndex = Math.Clamp(S.OverlayCorner, 0, corners.Length - 1);
            StartWithWindowsToggle.IsOn = StartupService.IsEnabled();
            StartMinimizedToggle.IsOn = S.StartMinimized;

            BackendCombo.Items.Clear();
            BackendCombo.Items.Add(Item(L("Settings.BackendAuto"), AdlxBackendKind.Auto));
            BackendCombo.Items.Add(Item(L("Settings.BackendAmd"), AdlxBackendKind.AmdDll));
            BackendCombo.Items.Add(Item(L("Settings.BackendHelper"), AdlxBackendKind.HelperDll));
            BackendCombo.SelectedIndex = (int)S.Backend;

            GpuCombo.Items.Clear();
            foreach (var g in App.Hardware.AllGpus) GpuCombo.Items.Add(Item($"[{g.Index}] {g.Name}", g.Index));
            GpuCombo.IsEnabled = App.Hardware.AllGpus.Count > 1 && App.Hardware.AdlxAvailable;
            GpuCombo.Opacity = GpuCombo.IsEnabled ? 1 : 0.5;
            BackendCombo.IsEnabled = S.AdvancedMode;
            ApplyModeCombo.IsEnabled = S.AdvancedMode;
            GpuCombo.SelectedIndex = Math.Clamp(App.Hardware.CurrentGpu.Index, 0, Math.Max(0, GpuCombo.Items.Count - 1));

            IntervalCombo.Items.Clear();
            foreach (int ms in Intervals) IntervalCombo.Items.Add(Item($"{ms / 1000.0:0.#} s", ms));
            int idx = Array.IndexOf(Intervals, S.SensorIntervalMs);
            IntervalCombo.SelectedIndex = idx < 0 ? 1 : idx;

            AutoUpdateToggle.IsOn = S.CheckForUpdates;
            VersionText.Text = L("Settings.UpdateVersion", UpdateService.CurrentText);

            LiveWarnBar.Title = L("Settings.LiveWarnTitle");
            LiveWarnBar.Message = L("Settings.LiveWarnBanner");
            LoadDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            LogService.Error($"Settings page: {ex}");
        }
        finally
        {
            _loading = false;
        }
    }

    private async void LoadDiagnosticsAsync()
    {
        try
        {
            DiagBox.Text = await Task.Run(() => App.Hardware.BuildDiagnostics());
        }
        catch (Exception ex)
        {
            LogService.Error($"Diagnostics: {ex}");
            DiagBox.Text = ex.Message;
        }
    }

    private async void LiveUndervolt_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (LiveUndervoltToggle.IsOn && !S.LiveApplyUndervolt && !await ConfirmLiveUndervoltAsync())
        {
            _loading = true;
            try { LiveUndervoltToggle.IsOn = false; }
            finally { _loading = false; }
            return;
        }

        Toggle_Changed(sender, e);
    }

    private async Task<bool> ConfirmLiveUndervoltAsync()
    {
        var red = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE8, 0x4A, 0x4A));
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        title.Children.Add(new FontIcon { Glyph = "\uE7BA", FontSize = 34, Foreground = red });
        title.Children.Add(new TextBlock
        {
            Text = L("Settings.LiveWarnTitle"),
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = red,
            VerticalAlignment = VerticalAlignment.Center
        });

        var ack = new CheckBox { Content = L("Settings.LiveWarnAck") };
        var body = new StackPanel { Spacing = 16, MaxWidth = 540 };
        body.Children.Add(new TextBlock { Text = L("Settings.LiveWarnBody"), TextWrapping = TextWrapping.Wrap, FontSize = 15 });
        body.Children.Add(ack);

        var dlg = new ContentDialog
        { RequestedTheme = ThemeService.Element,
            XamlRoot = XamlRoot,
            Title = title,
            Content = body,
            PrimaryButtonText = L("Settings.LiveWarnEnable"),
            CloseButtonText = L("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };
        ack.Checked += (_, _) => dlg.IsPrimaryButtonEnabled = true;
        ack.Unchecked += (_, _) => dlg.IsPrimaryButtonEnabled = false;
        return await ThemeService.ShowAsync(dlg) == ContentDialogResult.Primary;
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string lang }) return;
        S.Language = lang;
        App.Settings.Save();
        DispatcherQueue.TryEnqueue(() => App.Localization.LoadLanguage(lang));
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedItem is not ComboBoxItem { Tag: AppTheme theme }) return;
        S.Theme = (int)theme;
        App.Settings.Save();
        ThemeService.Refresh();
    }

    private void ApplyMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ApplyModeCombo.SelectedItem is not ComboBoxItem { Tag: ApplyMode m }) return;
        S.GraphicsApplyMode = m;
        App.Settings.Save();
    }

    private void OverlaySeconds_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        if (double.IsNaN(sender.Value)) { sender.Value = 3; return; }
        S.OverlaySeconds = Math.Clamp(sender.Value, 1, 30);
        App.Settings.Save();
    }

    private void OverlayCorner_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || OverlayCornerCombo.SelectedItem is not ComboBoxItem { Tag: int c }) return;
        S.OverlayCorner = c;
        App.Settings.Save();
        ShowOverlayPreview();
    }

    private void OverlayTest_Click(object sender, RoutedEventArgs e) => ShowOverlayPreview();

    private static void ShowOverlayPreview()
    {
        var items = new List<OverlayItem>
        {
            new("Anti-Lag", L("Common.On"), true),
            new("AFMF", L("Common.On"), true),
            new("FRTC", "120 fps", true),
            new("Radeon Chill", L("Common.Off"), false),
        };
        OverlayService.Shared.Show(L("Notify.Title", "Red Control Utility"), items, S.OverlaySeconds, S.OverlayCorner);
    }

    private void Overheat_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        S.OverheatTempC = (int)OverheatSlider.Value;
        S.OverheatSeconds = (int)OverheatSecondsSlider.Value;
        RefreshOverheatUi();
        App.Settings.Save();
    }

    private void RefreshOverheatUi()
    {
        OverheatValueText.Text = $"{(int)OverheatSlider.Value} °C";
        OverheatSecondsText.Text = $"{(int)OverheatSecondsSlider.Value} s";
        OverheatOptions.Opacity = OverheatToggle.IsOn ? 1 : 0.5;
        OverheatSlider.IsEnabled = OverheatAlwaysToggle.IsEnabled = OverheatToggle.IsOn;
        OverheatSecondsSlider.IsEnabled = OverheatToggle.IsOn && !OverheatAlwaysToggle.IsOn;
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool modeChanged = S.AdvancedMode != AdvancedToggle.IsOn;
        S.AdvancedMode = AdvancedToggle.IsOn;
        S.AutoRevertTuning = AutoRevertToggle.IsOn;
        S.LiveApplyUndervolt = LiveUndervoltToggle.IsOn;
        S.MinimizeToTray = MinimizeToTrayToggle.IsOn;
        S.PerAppProfilesEnabled = PerAppToggle.IsOn;
        S.AutoResetOnInstability = AutoResetToggle.IsOn;
        S.OverheatWarn = OverheatToggle.IsOn;
        S.OverheatAlwaysOn = OverheatAlwaysToggle.IsOn;
        RefreshOverheatUi();
        S.NotifyOnProfile = NotifyToggle.IsOn;
        S.CheckForUpdates = AutoUpdateToggle.IsOn;
        S.StartMinimized = StartMinimizedToggle.IsOn;

        if (ReferenceEquals(sender, StartWithWindowsToggle) || ReferenceEquals(sender, StartMinimizedToggle))
        {
            bool ok = StartupService.SetEnabled(StartWithWindowsToggle.IsOn, StartMinimizedToggle.IsOn);
            if (!ok && StartWithWindowsToggle.IsOn) Show(StatusBar, InfoBarSeverity.Warning, L("Settings.StartupFailed"));
        }

        S.StartWithWindows = StartWithWindowsToggle.IsOn;
        App.Settings.Save();
        App.RestartEngine();
        if (modeChanged) App.RaiseModeChanged();
    }

    private void Backend_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || BackendCombo.SelectedItem is not ComboBoxItem { Tag: AdlxBackendKind k }) return;
        S.Backend = k;
        App.Settings.Save();
        Show(StatusBar, InfoBarSeverity.Informational, L("Settings.BackendChanged"));
    }

    private void Gpu_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || GpuCombo.SelectedItem is not ComboBoxItem { Tag: int idx }) return;
        S.GpuIndex = idx;
        App.Settings.Save();
        App.Hardware.SelectGpu(idx);
        LoadDiagnosticsAsync();
        Show(StatusBar, InfoBarSeverity.Success, L("Settings.GpuChanged"));
    }

    private void Interval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || IntervalCombo.SelectedItem is not ComboBoxItem { Tag: int ms }) return;
        S.SensorIntervalMs = ms;
        App.Settings.Save();
    }

    private async void Reinit_Click(object sender, RoutedEventArgs e)
    {
        App.Engine.Stop();
        FaultStore.Clear();
        await Task.Run(() => App.Hardware.Initialize(S.Backend, S.GpuIndex));
        App.RestartEngine();
        Load();
        Show(StatusBar, App.Hardware.AdlxAvailable ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
            App.Hardware.AdlxAvailable ? L("Settings.ReinitOk") : L("Settings.ReinitFail"));
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        VersionText.Text = L("Settings.UpdateChecking");
        try
        {
            var (info, error) = await UpdateService.CheckAsync();
            if (info == null)
            {
                VersionText.Text = L("Settings.UpdateVersion", UpdateService.CurrentText);
                Show(StatusBar, InfoBarSeverity.Warning, L("Settings.UpdateFailed"), error ?? string.Empty);
                return;
            }

            if (UpdateService.Available is not { } newer)
            {
                VersionText.Text = L("Settings.UpdateLatest", UpdateService.CurrentText);
                return;
            }

            string v = newer.Version.ToString(3);
            VersionText.Text = L("Settings.UpdateFound", v);
            var answer = await ConfirmAsync(XamlRoot, L("Update.Available", v), L("Update.Confirm", v), L("Update.Download"), L("Common.Cancel"));
            if (answer != ContentDialogResult.Primary) return;

            await RunUpdateAsync(XamlRoot, newer);
        }
        finally
        {
            _checkingUpdate = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async void ClearShader_Click(object sender, RoutedEventArgs e)
    {
        int n = await Task.Run(ShaderCacheService.ClearShaderCaches);
        var r = await Task.Run(() => App.Hardware.ResetDriverShaderCache());
        Show(StatusBar, InfoBarSeverity.Success, L("Settings.ShaderCleared", n, r.Succeeded() ? L("Common.Yes") : L("Common.No")));
    }

    private async void CopyDiag_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try { text = await Task.Run(() => App.Hardware.BuildDiagnostics()); }
        catch (Exception ex) { text = ex.Message; }
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        Show(StatusBar, InfoBarSeverity.Success, L("Settings.Copied"));
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = await Task.Run(() => ReportService.Create(App.Hardware.BuildDiagnostics()));
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });
            Show(StatusBar, InfoBarSeverity.Success, L("Settings.ReportDone"), path);
        }
        catch (Exception ex)
        {
            LogService.Error($"Report: {ex.Message}");
            Show(StatusBar, InfoBarSeverity.Error, L("Settings.ReportFailed"), ex.Message);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = AppPaths.LogDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Show(StatusBar, InfoBarSeverity.Error, L("Settings.OpenLogsFailed"), ex.Message);
        }
    }
}
