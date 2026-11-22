using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using RedControlUtility.Models;
using RedControlUtility.Services.Adlx;
using static RedControlUtility.UiHelper;

namespace RedControlUtility.Views;

public sealed partial class DisplayPage : Page
{
    private static readonly string[] ProfileKeys = { "Default", "Vivid", "Cooler", "Warmer", "FakeHdr", "FakeOledIps", "FakeOledVa", "Custom" };

    private List<DisplayInfo> _displays = new();
    private DisplayInfo? _display;
    private bool _loading = true;
    private bool _busy;
    private Debouncer? _driverDebounce;
    private Debouncer? _gammaDebounce;

    private bool Instant => App.Settings.Current.GraphicsApplyMode == ApplyMode.Instant;

    public DisplayPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _driverDebounce = new Debouncer(DispatcherQueue, 450, () => _ = ApplyAsync());
        _gammaDebounce = new Debouncer(DispatcherQueue, 120, ApplyGamma);
        App.Localization.LanguageChanged += LoadAll;
        LoadAll();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _driverDebounce?.Cancel();
        _gammaDebounce?.Cancel();
        App.Localization.LanguageChanged -= LoadAll;
    }

    private void LoadAll()
    {
        _loading = true;
        try
        {
            ApplyButton.Visibility = Instant ? Visibility.Collapsed : Visibility.Visible;
            PendingText.Text = string.Empty;

            ScalingModeCombo.Items.Clear();
            ScalingModeCombo.Items.Add(Item(L("Scale.Preserve"), (int)AdlxScaleMode.PreserveAspectRatio));
            ScalingModeCombo.Items.Add(Item(L("Scale.Full"), (int)AdlxScaleMode.FullPanel));
            ScalingModeCombo.Items.Add(Item(L("Scale.Centered"), (int)AdlxScaleMode.Centered));

            ColorProfileCombo.Items.Clear();
            foreach (var k in ProfileKeys) ColorProfileCombo.Items.Add(Item(L($"ColorProfile.{k}"), k));
            DriverPresetCombo.Items.Clear();
            foreach (var k in DriverPresets.Keys) DriverPresetCombo.Items.Add(Item(L($"DriverPreset.{k}"), k));
            DriverPresetCombo.PlaceholderText = L("Display.DriverPresetPlaceholder");
            DriverPresetCombo.SelectedIndex = -1;

            _displays = App.Hardware.ReadDisplays();
            DisplayCombo.Items.Clear();
            foreach (var d in _displays) DisplayCombo.Items.Add(Item(d.Title, d.Index));
            DisplayCombo.IsEnabled = _displays.Count > 0;

            if (_displays.Count > 0)
            {
                DisplayCombo.SelectedIndex = Math.Clamp(_display?.Index ?? 0, 0, _displays.Count - 1);
                FillDisplay(_displays[DisplayCombo.SelectedIndex]);
            }
            else
            {
                _display = null;
                FillDisplay(null);
                Show(StatusBar, InfoBarSeverity.Informational, L("Display.NoneFound"));
            }

            var p = App.CurrentProfile;
            SelectProfile(p.ColorProfile);
            SetGammaSliders(p);
        }
        finally
        {
            _loading = false;
        }
    }

    private void DisplayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DisplayCombo.SelectedIndex < 0 || DisplayCombo.SelectedIndex >= _displays.Count) return;
        _loading = true;
        FillDisplay(_displays[DisplayCombo.SelectedIndex]);
        _loading = false;
    }

    private void FillDisplay(DisplayInfo? d)
    {
        _display = d;
        var c = d?.Caps ?? new DisplayCapabilities();
        var s = d?.Current ?? new DisplaySettings();

        FreeSyncToggle.Visibility = c.FreeSync ? Visibility.Visible : Visibility.Collapsed;
        FreeSyncToggle.IsOn = s.FreeSync == true;
        FreeSyncColorToggle.Visibility = c.FreeSyncColorAccuracy ? Visibility.Visible : Visibility.Collapsed;
        FreeSyncColorToggle.IsOn = s.FreeSyncColorAccuracy == true;
        bool hasRange = c.FreeSync && c.FreeSyncMaxHz > 0;
        FreeSyncRangeText.Visibility = hasRange ? Visibility.Visible : Visibility.Collapsed;
        FreeSyncRangeText.Text = hasRange ? L("Display.FreeSyncRange", (int)Math.Round(c.FreeSyncMinHz), (int)Math.Round(c.FreeSyncMaxHz)) : string.Empty;
        VsrToggle.IsEnabled = c.Vsr; VsrToggle.IsOn = s.Vsr == true;
        GpuScalingToggle.IsEnabled = c.GpuScaling; GpuScalingToggle.IsOn = s.GpuScaling == true;
        IntegerScalingToggle.IsEnabled = c.IntegerScaling; IntegerScalingToggle.IsOn = s.IntegerScaling == true;

        ScalingModeCombo.IsEnabled = c.ScalingMode;
        ScalingModeCombo.SelectedIndex = -1;
        if (s.ScalingMode != null)
        {
            for (int i = 0; i < ScalingModeCombo.Items.Count; i++)
            {
                if (ScalingModeCombo.Items[i] is ComboBoxItem { Tag: int t } && t == s.ScalingMode) ScalingModeCombo.SelectedIndex = i;
            }
        }

        ColorSlider(HueSlider, HueValue, c.Hue, c.HueRange, s.Hue);
        ColorSlider(SatSlider, SatValue, c.Saturation, c.SaturationRange, s.Saturation);
        ColorSlider(BriSlider, BriValue, c.Brightness, c.BrightnessRange, s.Brightness);
        ColorSlider(ConSlider, ConValue, c.Contrast, c.ContrastRange, s.Contrast);
        ColorSlider(TempSlider, TempValue, c.Temperature, c.TemperatureRange, s.Temperature);
        DriverPresetCombo.IsEnabled = c.Hue || c.Saturation || c.Brightness || c.Contrast || c.Temperature;
        DriverPresetCombo.SelectedIndex = -1;
    }

    private static void ColorSlider(Slider sl, TextBlock label, bool supported, AdlxRange range, int? value)
    {
        sl.IsEnabled = supported;
        sl.Minimum = range.IsValid ? range.Min : 0;
        sl.Maximum = range.IsValid ? range.Max : 100;
        sl.StepFrequency = Math.Max(1, range.Step);
        sl.Value = Math.Clamp(value ?? (sl.Minimum + sl.Maximum) / 2, sl.Minimum, sl.Maximum);
        label.Text = supported ? ((int)sl.Value).ToString() : "—";
    }

    private DisplaySettings Collect()
    {
        var c = _display?.Caps ?? new DisplayCapabilities();
        var s = new DisplaySettings();
        if (c.FreeSync) s.FreeSync = FreeSyncToggle.IsOn;
        if (c.FreeSyncColorAccuracy) s.FreeSyncColorAccuracy = FreeSyncColorToggle.IsOn;
        if (c.Vsr) s.Vsr = VsrToggle.IsOn;
        if (c.GpuScaling) s.GpuScaling = GpuScalingToggle.IsOn;
        if (c.IntegerScaling) s.IntegerScaling = IntegerScalingToggle.IsOn;
        if (c.ScalingMode && ScalingModeCombo.SelectedItem is ComboBoxItem { Tag: int t }) s.ScalingMode = t;
        if (c.Hue) s.Hue = (int)HueSlider.Value;
        if (c.Saturation) s.Saturation = (int)SatSlider.Value;
        if (c.Brightness) s.Brightness = (int)BriSlider.Value;
        if (c.Contrast) s.Contrast = (int)ConSlider.Value;
        if (c.Temperature) s.Temperature = (int)TempSlider.Value;
        return s;
    }

    private static bool Ne<T>(T? cur, T? want) where T : struct => want.HasValue && !EqualityComparer<T>.Default.Equals(cur ?? default(T), want.Value);

    private static DisplaySettings Diff(DisplaySettings c, DisplaySettings d)
    {
        var r = new DisplaySettings();
        if (Ne(c.FreeSync, d.FreeSync)) r.FreeSync = d.FreeSync;
        if (Ne(c.FreeSyncColorAccuracy, d.FreeSyncColorAccuracy)) r.FreeSyncColorAccuracy = d.FreeSyncColorAccuracy;
        if (Ne(c.Vsr, d.Vsr)) r.Vsr = d.Vsr;
        if (Ne(c.GpuScaling, d.GpuScaling)) r.GpuScaling = d.GpuScaling;
        if (Ne(c.IntegerScaling, d.IntegerScaling)) r.IntegerScaling = d.IntegerScaling;
        if (Ne(c.ScalingMode, d.ScalingMode)) r.ScalingMode = d.ScalingMode;
        if (Ne(c.Hue, d.Hue)) r.Hue = d.Hue;
        if (Ne(c.Saturation, d.Saturation)) r.Saturation = d.Saturation;
        if (Ne(c.Brightness, d.Brightness)) r.Brightness = d.Brightness;
        if (Ne(c.Contrast, d.Contrast)) r.Contrast = d.Contrast;
        if (Ne(c.Temperature, d.Temperature)) r.Temperature = d.Temperature;
        return r;
    }

    private static bool IsEmpty(DisplaySettings s) =>
        s.FreeSync == null && s.FreeSyncColorAccuracy == null && s.Vsr == null && s.GpuScaling == null && s.IntegerScaling == null && s.ScalingMode == null &&
        s.Hue == null && s.Saturation == null && s.Brightness == null && s.Contrast == null && s.Temperature == null;

    private void DriverChanged()
    {
        if (_loading || _display == null) return;
        if (Instant) _driverDebounce?.Trigger();
        else PendingText.Text = IsEmpty(Diff(_display.Current, Collect())) ? string.Empty : L("Common.PendingChanges");
    }

    private void Driver_Changed(object sender, RoutedEventArgs e) => DriverChanged();
    private void Driver_SelectionChanged(object sender, SelectionChangedEventArgs e) => DriverChanged();

    private static readonly Dictionary<string, (int Hue, int Sat, int Bri, int Con, int Temp)> DriverPresets = new()
    {
        ["Default"] = (0, 100, 0, 100, 6500),
        ["FakeHdr"] = (0, 118, 2, 112, 6500),
        ["FakeOledIps"] = (0, 112, -3, 118, 6400),
        ["FakeOledVa"] = (0, 108, 0, 110, 6500),
    };

    private void DriverPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DriverPresetCombo.SelectedItem is not ComboBoxItem { Tag: string key } || !DriverPresets.TryGetValue(key, out var p)) return;
        SetIfEnabled(HueSlider, p.Hue);
        SetIfEnabled(SatSlider, p.Sat);
        SetIfEnabled(BriSlider, p.Bri);
        SetIfEnabled(ConSlider, p.Con);
        SetIfEnabled(TempSlider, p.Temp);
    }

    private static void SetIfEnabled(Slider s, int value)
    {
        if (s.IsEnabled) s.Value = Math.Clamp(value, s.Minimum, s.Maximum);
    }

    private void Driver_Slider(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (ReferenceEquals(sender, HueSlider)) HueValue.Text = ((int)HueSlider.Value).ToString();
        else if (ReferenceEquals(sender, SatSlider)) SatValue.Text = ((int)SatSlider.Value).ToString();
        else if (ReferenceEquals(sender, BriSlider)) BriValue.Text = ((int)BriSlider.Value).ToString();
        else if (ReferenceEquals(sender, ConSlider)) ConValue.Text = ((int)ConSlider.Value).ToString();
        else if (ReferenceEquals(sender, TempSlider)) TempValue.Text = ((int)TempSlider.Value).ToString();
        DriverChanged();
    }

    private void RefreshDisplayState()
    {
        int keep = _display?.Index ?? 0;
        _loading = true;
        try
        {
            _displays = App.Hardware.ReadDisplays();
            if (_displays.Count == 0) return;
            keep = Math.Clamp(keep, 0, _displays.Count - 1);
            FillDisplay(_displays[keep]);
            PendingText.Text = string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        LoadAll();
        StatusBar.IsOpen = false;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e) => await ApplyAsync();

    private async Task ApplyAsync()
    {
        if (_busy || _loading || _display == null) return;
        var diff = Diff(_display.Current, Collect());
        if (IsEmpty(diff))
        {
            Show(StatusBar, InfoBarSeverity.Informational, L("Msg.NothingToApply"));
            return;
        }

        _busy = true;
        try
        {
            int idx = _display.Index;
            var report = await Task.Run(() => App.Hardware.ApplyDisplay(idx, diff));
            ShowReport(StatusBar, report);
            if (report.AnyOk) App.DisplayPersistence.Remember(_display, diff);
            RefreshDisplayState();
        }
        finally
        {
            _busy = false;
        }
    }

    private void SelectProfile(string name)
    {
        for (int i = 0; i < ColorProfileCombo.Items.Count; i++)
        {
            if (ColorProfileCombo.Items[i] is ComboBoxItem { Tag: string t } && t == name)
            {
                ColorProfileCombo.SelectedIndex = i;
                return;
            }
        }

        ColorProfileCombo.SelectedIndex = 0;
    }

    private void SetGammaSliders(UtilityProfile p)
    {
        GammaSlider.Minimum = 0.5; GammaSlider.Maximum = 2.0; GammaSlider.StepFrequency = 0.01; GammaSlider.Value = p.CustomGamma;
        foreach (var s in new[] { RedSlider, GreenSlider, BlueSlider })
        {
            s.Minimum = 0.5; s.Maximum = 1.2; s.StepFrequency = 0.01;
        }

        RedSlider.Value = p.CustomRed; GreenSlider.Value = p.CustomGreen; BlueSlider.Value = p.CustomBlue;
        UpdateGammaLabels();
        bool custom = p.ColorProfile == "Custom";
        foreach (var s in new[] { GammaSlider, RedSlider, GreenSlider, BlueSlider }) s.IsEnabled = custom;
    }

    private void UpdateGammaLabels()
    {
        GammaValue.Text = GammaSlider.Value.ToString("0.00");
        RedValue.Text = RedSlider.Value.ToString("0.00");
        GreenValue.Text = GreenSlider.Value.ToString("0.00");
        BlueValue.Text = BlueSlider.Value.ToString("0.00");
    }

    private void ColorProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ColorProfileCombo.SelectedItem is not ComboBoxItem { Tag: string name }) return;
        App.CurrentProfile.ColorProfile = name;
        bool custom = name == "Custom";
        foreach (var s in new[] { GammaSlider, RedSlider, GreenSlider, BlueSlider }) s.IsEnabled = custom;
        _gammaDebounce?.Trigger();
    }

    private void Gamma_Slider(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateGammaLabels();
        if (_loading) return;
        var p = App.CurrentProfile;
        p.CustomGamma = GammaSlider.Value;
        p.CustomRed = RedSlider.Value;
        p.CustomGreen = GreenSlider.Value;
        p.CustomBlue = BlueSlider.Value;
        _gammaDebounce?.Trigger();
    }

    private void ApplyGamma()
    {
        var p = App.CurrentProfile;
        bool ok = p.ColorProfile == "Custom"
            ? App.DisplayColor.ApplyCustomColor(p.CustomGamma, p.CustomRed, p.CustomGreen, p.CustomBlue)
            : App.DisplayColor.ApplyColorProfile(p.ColorProfile);
        if (!ok) Show(StatusBar, InfoBarSeverity.Warning, L("Display.GammaFailed"));
    }
}
