using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using RedControlUtility.Models;
using RedControlUtility.Services;
using RedControlUtility.Services.Adlx;
using static RedControlUtility.UiHelper;

namespace RedControlUtility.Views;

public sealed partial class TuningPage : Page
{
    private TuningCapabilities _caps = new();
    private TuningSettings _current = new();
    private List<VfCurvePoint> _states = new();
    private List<FanCurvePoint> _fan = new();
    private readonly List<NumberBox> _stateBoxes = new();
    private readonly List<NumberBox> _fanBoxes = new();
    private List<FanCurvePoint>? _driverFan;
    private const string FanPresetDriver = "driver", FanPresetOptimized = "optimized", FanPresetCustom = "custom";
    private bool _loading = true;
    private int _baseVoltage;
    private double _ghostMax, _ghostVolt;
    private List<VfCurvePoint> _baseStates = new();
    private const int MinBelowMaxMhz = 100;
    private bool _busy;
    private Debouncer? _liveDebounce;

    public TuningPage()
    {
        InitializeComponent();
        FanChart.Draggable = true;
        FanChart.PointDragged += FanDragged;
        VfChart.Selectable = true;
        VfChart.SelectionShifted += StatesShifted;
        MmChart.Selectable = true;
        MmChart.SelectionShifted += MmShifted;
        VfChart.CanMoveFreely = _ => true;
        VfChart.PointMoved += StateMovedFree;
        MmChart.CanMoveFreely = i => i == MmChartLastIndex;
        MmChart.PointMoved += MmMovedFree;
    }

    private const int MmChartLastIndex = 8;

    private void StateMovedFree(int i, double x, double y)
    {
        if (i < 0 || i >= _states.Count) return;
        _states[i].FrequencyMHz = (int)Math.Round(x);
        _states[i].VoltageMv = (int)Math.Round(y);
        bool was = _loading;
        _loading = true;
        if (_stateBoxes.Count > 2 * i + 1)
        {
            _stateBoxes[2 * i].Value = _states[i].FrequencyMHz;
            _stateBoxes[2 * i + 1].Value = _states[i].VoltageMv;
        }

        _loading = was;
        RefreshVfChart();
        MarkPending();
    }

    private void MmMovedFree(int i, double x, double y)
    {
        if (!double.IsNaN(x)) GpuMaxBox.Value = Math.Clamp(Math.Round(x), GpuMaxBox.Minimum, GpuMaxBox.Maximum);
        VoltageSlider.Value = Math.Clamp(Math.Round(y), VoltageSlider.Minimum, VoltageSlider.Maximum);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _liveDebounce = new Debouncer(DispatcherQueue, 600, () => _ = ApplyAsync(skipConfirm: true));
        App.Localization.LanguageChanged += LoadAll;
        LoadAll();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _liveDebounce?.Cancel();
        App.Localization.LanguageChanged -= LoadAll;
    }

    private static NumberBox Box(double min, double max, double value, double width = 130)
    {
        return new NumberBox
        {
            Minimum = min,
            Maximum = Math.Max(max, min + 1),
            Value = Math.Clamp(value, min, Math.Max(max, min + 1)),
            Width = width,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten
        };
    }

    private static void SetupNumber(NumberBox nb, AdlxRange r, int? value, int fallback)
    {
        nb.Minimum = r.IsValid ? r.Min : 0;
        nb.Maximum = r.IsValid ? r.Max : 100000;
        nb.SmallChange = Math.Max(1, r.Step);
        nb.Value = Math.Clamp(value ?? fallback, nb.Minimum, nb.Maximum);
    }

    private static void SetupSlider(Slider sl, AdlxRange r, int? value, int fallback)
    {
        sl.Minimum = r.IsValid ? r.Min : 0;
        sl.Maximum = r.IsValid ? r.Max : 100;
        sl.StepFrequency = Math.Max(1, r.Step);
        sl.Value = Math.Clamp(value ?? fallback, sl.Minimum, sl.Maximum);
    }

    private void LoadAll()
    {
        _loading = true;
        try
        {
            _caps = App.Hardware.Caps.Tuning;
            bool any = _caps.AnyTuning;
            UnsupportedText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            ApplyButton.IsEnabled = ReloadButton.IsEnabled = FactoryButton.IsEnabled = any;
            foreach (var c in new UIElement[] { CoreCard, VramCard, FanCard })
                c.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

            if (!any)
            {
                StatesCard.Visibility = Visibility.Collapsed;
                return;
            }

            _current = App.Hardware.ReadTuning();
            var t = _caps;
            var s = _current;


            bool minMax = t.GfxMode == GfxTuningMode.MinMaxVoltage;
            CoreCard.Visibility = minMax || t.PowerSupported ? Visibility.Visible : Visibility.Collapsed;
            MinMaxPanel.Visibility = minMax ? Visibility.Visible : Visibility.Collapsed;
            VoltagePanel.Visibility = minMax && t.VoltageRange.IsValid ? Visibility.Visible : Visibility.Collapsed;
            OffsetPanel.Visibility = VoltagePanel.Visibility;
            MmChartPanel.Visibility = minMax && t.VoltageRange.IsValid ? Visibility.Visible : Visibility.Collapsed;
            KeepMinToggle.Visibility = minMax ? Visibility.Visible : Visibility.Collapsed;
            KeepMinToggle.IsOn = App.Settings.Current.KeepMinClockBelowMax;
            if (minMax)
            {
                SetupNumber(GpuMinBox, t.MinFreqRange, s.GpuMinMhz, t.DefaultMinFreq ?? t.MinFreqRange.Min);
                SetupNumber(GpuMaxBox, t.MaxFreqRange, s.GpuMaxMhz, t.DefaultMaxFreq ?? t.MaxFreqRange.Max);
                SetupSlider(VoltageSlider, t.VoltageRange, s.VoltageMv, t.DefaultVoltage ?? t.VoltageRange.Max);
                _baseVoltage = (int)VoltageSlider.Value;
                _ghostVolt = VoltageSlider.Value;
                _ghostMax = double.IsNaN(GpuMaxBox.Value) ? 0 : GpuMaxBox.Value;
                if (t.DefaultVoltage.HasValue) _baseVoltage = t.DefaultVoltage.Value;
                UpdateVoltageLabel();
                GpuMinBox.IsEnabled = !KeepMinToggle.IsOn;
                if (KeepMinToggle.IsOn) SyncMin();
                RefreshMmChart();
            }

            PowerPanel.Visibility = t.PowerSupported ? Visibility.Visible : Visibility.Collapsed;
            if (t.PowerSupported)
            {
                SetupSlider(PowerSlider, t.PowerRange, s.PowerLimitPercent, t.PowerDefault ?? 0);
                UpdatePowerLabel();
            }

            bool stateList = t.GfxMode == GfxTuningMode.StateList && s.VfStates is { Count: > 0 };
            StatesCard.Visibility = stateList ? Visibility.Visible : Visibility.Collapsed;
            _states.Clear();
            _baseStates.Clear();
            if (stateList)
            {
                _states = s.VfStates!.Select(p => p.Clone()).ToList();
                _baseStates = s.VfStates!.Select(p => p.Clone()).ToList();
                BuildStateRows();
            }

            VramCard.Visibility = t.VramSupported ? Visibility.Visible : Visibility.Collapsed;
            VramBox.Visibility = t.VramFreqSupported ? Visibility.Visible : Visibility.Collapsed;
            VramTimingCombo.Visibility = t.VramTimingSupported ? Visibility.Visible : Visibility.Collapsed;
            if (t.VramFreqSupported) SetupNumber(VramBox, t.VramFreqRange, s.VramMaxMhz, t.VramFreqRange.Max);
            VramTimingCombo.Items.Clear();
            foreach (var tm in t.VramTimings) VramTimingCombo.Items.Add(Item(L($"Timing.{tm}"), (int)tm));
            VramTimingCombo.SelectedIndex = 0;
            for (int i = 0; i < VramTimingCombo.Items.Count; i++)
                if (VramTimingCombo.Items[i] is ComboBoxItem { Tag: int tg } && s.VramTiming != null && tg == (int)s.VramTiming) VramTimingCombo.SelectedIndex = i;

            FanCard.Visibility = Visibility.Visible;
            bool hasCurve = t.FanCurveSupported && s.FanCurve is { Count: > 0 };
            FanChart.Visibility = FanRows.Visibility = FanPresetCombo.Visibility = hasCurve ? Visibility.Visible : Visibility.Collapsed;
            ZeroRpmToggle.Visibility = s.ZeroRpm != null && t.ZeroRpmSupported ? Visibility.Visible : Visibility.Collapsed;
            ZeroRpmToggle.IsOn = s.ZeroRpm == true;
            bool hasMinimum = t.FanMinRpmRange.IsValid && s.FanMinRpm != null;
            bool hasTarget = t.FanTargetRpmRange.IsValid && s.FanTargetRpm != null;
            FanRpmPanel.Visibility = hasMinimum || hasTarget ? Visibility.Visible : Visibility.Collapsed;
            FanUnsupportedText.Visibility = hasCurve || hasMinimum || hasTarget || ZeroRpmToggle.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            FanMinRpmBox.Visibility = hasMinimum ? Visibility.Visible : Visibility.Collapsed;
            FanTargetRpmBox.Visibility = hasTarget ? Visibility.Visible : Visibility.Collapsed;
            if (hasMinimum) SetupNumber(FanMinRpmBox, t.FanMinRpmRange, s.FanMinRpm, t.FanMinRpmRange.Min);
            if (hasTarget) SetupNumber(FanTargetRpmBox, t.FanTargetRpmRange, s.FanTargetRpm, t.FanTargetRpmRange.Min);
            _fan = s.FanCurve?.Select(p => p.Clone()).ToList() ?? new();
            if (hasCurve)
            {
                _driverFan = (App.Hardware.TuningBaseline?.FanCurve ?? _fan).Select(p => p.Clone()).ToList();
                BuildFanRows();
                FillFanPresets();
            }

            if (!Adv)
            {
                if (!IsPolaris) StatesCard.Visibility = Visibility.Collapsed;
                VramCard.Visibility = Visibility.Collapsed;
            }

            PendingText.Text = string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private void BuildStateRows()
    {
        StatesRows.Children.Clear();
        _stateBoxes.Clear();
        var t = _caps;
        for (int i = 0; i < _states.Count; i++)
        {
            int idx = i;
            var p = _states[i];
            var f = Box(t.StateFreqRange.IsValid ? t.StateFreqRange.Min : 0, t.StateFreqRange.IsValid ? t.StateFreqRange.Max : 5000, p.FrequencyMHz, 140);
            var v = Box(t.StateVoltageRange.IsValid ? t.StateVoltageRange.Min : 0, t.StateVoltageRange.IsValid ? t.StateVoltageRange.Max : 1500, p.VoltageMv, 140);
            f.SmallChange = Math.Max(1, t.StateFreqRange.Step);
            v.SmallChange = Math.Max(1, t.StateVoltageRange.Step);
            f.ValueChanged += (_, _) => { if (_loading || double.IsNaN(f.Value)) return; _states[idx].FrequencyMHz = (int)f.Value; StatesEdited(); };
            v.ValueChanged += (_, _) => { if (_loading || double.IsNaN(v.Value)) return; _states[idx].VoltageMv = (int)v.Value; StatesEdited(); };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new TextBlock { Text = L("Tuning.State", p.Index), Width = 80, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(f);
            row.Children.Add(new TextBlock { Text = "MHz", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
            row.Children.Add(v);
            row.Children.Add(new TextBlock { Text = "mV", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
            StatesRows.Children.Add(row);
            _stateBoxes.Add(f);
            _stateBoxes.Add(v);
        }

        VfChart.SetAxes(
            _caps.StateFreqRange.IsValid ? _caps.StateFreqRange.Min : 0, _caps.StateFreqRange.IsValid ? _caps.StateFreqRange.Max : 3000,
            _caps.StateVoltageRange.IsValid ? _caps.StateVoltageRange.Min : 600, _caps.StateVoltageRange.IsValid ? _caps.StateVoltageRange.Max : 1300,
            "MHz", "mV");
        RefreshVfChart();
    }

    private void RefreshVfChart()
    {
        VfChart.SetPoints(_states.Select(p => ((double)p.FrequencyMHz, (double)p.VoltageMv)));
        VfChart.SetGhost(_baseStates.Select(p => ((double)p.FrequencyMHz, (double)p.VoltageMv)));
    }

    private void StatesShifted(IReadOnlyList<int> indices, double dy)
    {
        double lo = _caps.StateVoltageRange.IsValid ? _caps.StateVoltageRange.Min : 0;
        double hi = _caps.StateVoltageRange.IsValid ? _caps.StateVoltageRange.Max : 2000;
        double fLo = _caps.StateFreqRange.IsValid ? _caps.StateFreqRange.Min : 0;
        double fHi = _caps.StateFreqRange.IsValid ? _caps.StateFreqRange.Max : 5000;
        bool was = _loading;
        _loading = true;
        foreach (int i in indices)
        {
            if (i < 0 || i >= _states.Count) continue;
            double oldV = _states[i].VoltageMv;
            int newV = (int)Math.Clamp(oldV + dy, lo, hi);
            double ratio = oldV > 0 ? newV / oldV : 1;
            int newF = (int)Math.Clamp(Math.Round(_states[i].FrequencyMHz * ratio), fLo, fHi);
            _states[i].VoltageMv = newV;
            _states[i].FrequencyMHz = newF;
            if (_stateBoxes.Count > 2 * i + 1) { _stateBoxes[2 * i].Value = newF; _stateBoxes[2 * i + 1].Value = newV; }
        }

        _loading = was;
        RefreshVfChart();
        MarkPending();
    }

    private void MmShifted(IReadOnlyList<int> indices, double dy)
    {
        double oldV = VoltageSlider.Value;
        double newV = Math.Clamp(oldV + dy, VoltageSlider.Minimum, VoltageSlider.Maximum);
        if (oldV > 0 && !double.IsNaN(GpuMaxBox.Value))
            GpuMaxBox.Value = Math.Clamp(Math.Round(GpuMaxBox.Value * newV / oldV), GpuMaxBox.Minimum, GpuMaxBox.Maximum);
        VoltageSlider.Value = newV;
    }

    private void StatesEdited()
    {
        RefreshVfChart();
        MarkPending();
    }

    private void BuildFanRows()
    {
        FanRows.Children.Clear();
        _fanBoxes.Clear();
        var t = _caps;
        double tMin = t.FanTempRange.IsValid ? t.FanTempRange.Min : 0, tMax = t.FanTempRange.IsValid ? t.FanTempRange.Max : 110;
        double sMin = t.FanSpeedRange.IsValid ? t.FanSpeedRange.Min : 0, sMax = t.FanSpeedRange.IsValid ? t.FanSpeedRange.Max : 100;
        for (int i = 0; i < _fan.Count; i++)
        {
            int idx = i;
            var p = _fan[i];
            var tb = Box(tMin, tMax, p.TemperatureC);
            var sb = Box(sMin, sMax, p.FanSpeedPercent);
            tb.ValueChanged += (_, _) => { if (_loading || double.IsNaN(tb.Value)) return; _fan[idx].TemperatureC = (int)tb.Value; NormalizeFan(idx); MarkPending(); };
            sb.ValueChanged += (_, _) => { if (_loading || double.IsNaN(sb.Value)) return; _fan[idx].FanSpeedPercent = (int)sb.Value; NormalizeFan(idx); MarkPending(); };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new TextBlock { Text = L("Tuning.Point", idx + 1), Width = 80, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(tb);
            row.Children.Add(new TextBlock { Text = "°C", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
            row.Children.Add(sb);
            row.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
            FanRows.Children.Add(row);
            _fanBoxes.Add(tb);
            _fanBoxes.Add(sb);
        }

        FanChart.SetAxes(tMin, tMax, sMin, sMax, "°C", "%");
        RefreshFanChart();
    }

    private void RefreshFanChart() => FanChart.SetPoints(_fan.Select(p => ((double)p.TemperatureC, (double)p.FanSpeedPercent)));

    private void UpdateVoltageLabel()
    {
        int v = (int)VoltageSlider.Value;
        int? def = _caps.DefaultVoltage;
        VoltageValue.Text = def.HasValue ? $"{v} mV  ({v - def.Value:+0;-0;0} mV)" : $"{v} mV";
    }

    private void UpdatePowerLabel() => PowerValue.Text = $"{(int)PowerSlider.Value:+0;-0;0} %";

    private void Voltage_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateVoltageLabel();
        RefreshMmChart();
        Changed(live: true);
    }

    private void Power_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdatePowerLabel();
        Changed(live: true);
    }

    private void Field_SelectionChanged(object sender, SelectionChangedEventArgs e) => Changed(false);
    private void Field_NumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loading && ReferenceEquals(sender, GpuMaxBox) && KeepMinToggle.IsOn) SyncMin();
        if (!_loading) RefreshMmChart();
        Changed(false);
    }

    private void SyncMin()
    {
        if (double.IsNaN(GpuMaxBox.Value)) return;
        double target = Math.Clamp(GpuMaxBox.Value - MinBelowMaxMhz, GpuMinBox.Minimum, GpuMinBox.Maximum);
        bool was = _loading;
        _loading = true;
        GpuMinBox.Value = target;
        _loading = was;
    }

    private static List<(double X, double Y)> CurvePoints(double fStart, double fmax, double vLo, double vlim)
    {
        var pts = new List<(double X, double Y)>();
        if (fmax <= fStart) fmax = fStart + 1;
        for (int i = 0; i <= 8; i++)
        {
            double k = i / 8.0;
            pts.Add((fStart + (fmax - fStart) * k, vLo + (vlim - vLo) * Math.Pow(k, 1.6)));
        }

        return pts;
    }

    private void RefreshMmChart()
    {
        var t = _caps;
        if (t.GfxMode != GfxTuningMode.MinMaxVoltage || !t.VoltageRange.IsValid) return;
        double fLo = t.MinFreqRange.IsValid ? t.MinFreqRange.Min : 500;
        double fHi = t.MaxFreqRange.IsValid ? t.MaxFreqRange.Max : 3000;
        double vLo = t.VoltageRange.Min, vHi = t.VoltageRange.Max;
        double fmax = double.IsNaN(GpuMaxBox.Value) ? fHi : GpuMaxBox.Value;
        double vlim = VoltageSlider.Value;
        MmChart.SetAxes(fLo, fHi, vLo, vHi, "MHz", "mV");
        MmChart.SetPoints(CurvePoints(fLo, fmax, vLo, vlim));
        double gmax = _ghostMax > 0 ? _ghostMax : fmax;
        double gv = _ghostVolt > 0 ? _ghostVolt : vlim;
        MmChart.SetGhost(CurvePoints(fLo, gmax, vLo, gv));
    }

    private void FillFanPresets()
    {
        FanPresetCombo.Items.Clear();
        FanPresetCombo.Items.Add(Item(L("Tuning.FanPresetDriver"), FanPresetDriver));
        FanPresetCombo.Items.Add(Item(L("Tuning.FanPresetOptimized"), FanPresetOptimized));
        FanPresetCombo.Items.Add(Item(L("Tuning.FanPresetCustom"), FanPresetCustom));
        FanPresetCombo.SelectedIndex = MatchFanPreset();
    }

    private int MatchFanPreset()
    {
        if (SameCurve(_fan, _driverFan)) return 0;
        if (SameCurve(_fan, OptimizedFan(_caps))) return 1;
        return 2;
    }

    private static bool SameCurve(List<FanCurvePoint> a, List<FanCurvePoint>? b)
    {
        if (b == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].TemperatureC != b[i].TemperatureC || a[i].FanSpeedPercent != b[i].FanSpeedPercent) return false;
        return true;
    }

    private static readonly (int Temp, int Speed)[] OptimizedAnchors = { (30, 0), (40, 30), (50, 40), (60, 55), (70, 75), (80, 100), (110, 100) };

    private static List<FanCurvePoint> OptimizedFan(TuningCapabilities t)
    {
        int n = Math.Max(2, t.FanPointCount > 0 ? t.FanPointCount : 5);
        int tMin = t.FanTempRange.IsValid ? t.FanTempRange.Min : 25;
        int tMax = t.FanTempRange.IsValid ? t.FanTempRange.Max : 100;
        int sMin = t.FanSpeedRange.IsValid ? t.FanSpeedRange.Min : 0;
        int sMax = t.FanSpeedRange.IsValid ? t.FanSpeedRange.Max : 100;
        int first = Math.Clamp(40, tMin, tMax), last = Math.Clamp(80, tMin, tMax);
        var list = new List<FanCurvePoint>();
        for (int i = 0; i < n; i++)
        {
            int temp = n == 1 ? last : (int)Math.Round(first + (last - first) * i / (double)(n - 1));
            if (i > 0 && temp <= list[i - 1].TemperatureC) temp = list[i - 1].TemperatureC + 1;
            double speed = OptimizedAnchors[^1].Speed;
            for (int k = 1; k < OptimizedAnchors.Length; k++)
            {
                var (t0, s0) = OptimizedAnchors[k - 1];
                var (t1, s1) = OptimizedAnchors[k];
                if (temp <= t1)
                {
                    speed = s0 + (s1 - s0) * (temp - t0) / (double)(t1 - t0);
                    break;
                }
            }

            int sp = (int)Math.Clamp(Math.Round(speed), sMin, sMax);
            if (i > 0 && sp < list[i - 1].FanSpeedPercent) sp = list[i - 1].FanSpeedPercent;
            list.Add(new FanCurvePoint { TemperatureC = temp, FanSpeedPercent = sp });
        }

        return list;
    }

    private void FanPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FanPresetCombo.SelectedItem is not ComboBoxItem { Tag: string key }) return;
        if (key == FanPresetCustom) return;
        var curve = key == FanPresetOptimized ? OptimizedFan(_caps) : _driverFan?.Select(p => p.Clone()).ToList();
        if (curve == null) return;
        bool was = _loading;
        _loading = true;
        try
        {
            _fan = curve;
            BuildFanRows();
        }
        finally
        {
            _loading = was;
        }

        MarkPending();
    }

    private void SetFanPresetCustom()
    {
        if (FanPresetCombo.Items.Count < 3 || FanPresetCombo.SelectedIndex == 2) return;
        bool was = _loading;
        _loading = true;
        FanPresetCombo.SelectedIndex = MatchFanPreset();
        _loading = was;
    }

    private void FanDragged(int i, double x, double y)
    {
        if (i < 0 || i >= _fan.Count) return;
        _fan[i].TemperatureC = (int)Math.Round(x);
        _fan[i].FanSpeedPercent = (int)Math.Round(y);
        NormalizeFan(i);
        MarkPending();
    }

    private void NormalizeFan(int pivot)
    {
        int n = _fan.Count;
        double tMin = _fanBoxes.Count > 0 ? _fanBoxes[0].Minimum : 0, tMax = _fanBoxes.Count > 0 ? _fanBoxes[0].Maximum : 110;
        double sMin = _fanBoxes.Count > 1 ? _fanBoxes[1].Minimum : 0, sMax = _fanBoxes.Count > 1 ? _fanBoxes[1].Maximum : 100;

        _fan[pivot].TemperatureC = (int)Math.Clamp(_fan[pivot].TemperatureC, tMin + pivot, tMax - (n - 1 - pivot));
        _fan[pivot].FanSpeedPercent = (int)Math.Clamp(_fan[pivot].FanSpeedPercent, sMin, sMax);

        for (int j = pivot + 1; j < n; j++)
        {
            if (_fan[j].TemperatureC <= _fan[j - 1].TemperatureC) _fan[j].TemperatureC = _fan[j - 1].TemperatureC + 1;
            if (_fan[j].FanSpeedPercent < _fan[j - 1].FanSpeedPercent) _fan[j].FanSpeedPercent = _fan[j - 1].FanSpeedPercent;
        }

        for (int j = pivot - 1; j >= 0; j--)
        {
            if (_fan[j].TemperatureC >= _fan[j + 1].TemperatureC) _fan[j].TemperatureC = _fan[j + 1].TemperatureC - 1;
            if (_fan[j].FanSpeedPercent > _fan[j + 1].FanSpeedPercent) _fan[j].FanSpeedPercent = _fan[j + 1].FanSpeedPercent;
        }

        bool was = _loading;
        _loading = true;
        for (int j = 0; j < n && _fanBoxes.Count > 2 * j + 1; j++)
        {
            _fanBoxes[2 * j].Value = _fan[j].TemperatureC;
            _fanBoxes[2 * j + 1].Value = _fan[j].FanSpeedPercent;
        }

        _loading = was;
        RefreshFanChart();
        SetFanPresetCustom();
    }

    private void KeepMin_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.Current.KeepMinClockBelowMax = KeepMinToggle.IsOn;
        App.Settings.Save();
        GpuMinBox.IsEnabled = !KeepMinToggle.IsOn;
        if (KeepMinToggle.IsOn) SyncMin();
        Changed(false);
    }

    private void Offset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int delta)) return;
        VoltageSlider.Value = Math.Clamp(_baseVoltage + delta, VoltageSlider.Minimum, VoltageSlider.Maximum);
    }

    private void StateOffset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int delta) || _baseStates.Count == 0) return;
        double lo = _caps.StateVoltageRange.IsValid ? _caps.StateVoltageRange.Min : 0;
        double hi = _caps.StateVoltageRange.IsValid ? _caps.StateVoltageRange.Max : 2000;
        _states = _baseStates.Select(p => { var c = p.Clone(); c.VoltageMv = (int)Math.Clamp(p.VoltageMv + delta, lo, hi); return c; }).ToList();
        bool was = _loading;
        _loading = true;
        BuildStateRows();
        _loading = was;
        MarkPending();
    }
    private void Field_Toggled(object sender, RoutedEventArgs e) => Changed(false);

    private void Changed(bool live)
    {
        if (_loading) return;
        MarkPending();
        if (live && App.Settings.Current.LiveApplyUndervolt) _liveDebounce?.Trigger();
    }

    private void MarkPending()
    {
        if (!_loading) PendingText.Text = L("Common.PendingChanges");
    }

    private static bool IsPolaris => App.Hardware.CurrentGpu.Architecture == GpuArchitecture.Polaris;

    private static bool Adv => App.Settings.Current.AdvancedMode;

    private TuningSettings Collect()
    {
        var t = _caps;
        var s = new TuningSettings();
        if (t.GfxMode == GfxTuningMode.MinMaxVoltage)
        {
            if (!double.IsNaN(GpuMinBox.Value)) s.GpuMinMhz = (int)GpuMinBox.Value;
            if (!double.IsNaN(GpuMaxBox.Value)) s.GpuMaxMhz = (int)GpuMaxBox.Value;
            if (t.VoltageRange.IsValid) s.VoltageMv = (int)VoltageSlider.Value;
        }
        else if (t.GfxMode == GfxTuningMode.StateList && _states.Count > 0 && (Adv || IsPolaris))
        {
            s.VfStates = _states.Select(p => p.Clone()).ToList();
        }

        if (t.PowerSupported) s.PowerLimitPercent = (int)PowerSlider.Value;
        if (Adv && t.VramFreqSupported && !double.IsNaN(VramBox.Value)) s.VramMaxMhz = (int)VramBox.Value;
        if (Adv && t.VramTimingSupported && VramTimingCombo.SelectedItem is ComboBoxItem { Tag: int tm }) s.VramTiming = (AdlxMemoryTiming)tm;
        if (t.FanSupported)
        {
            if (ZeroRpmToggle.Visibility == Visibility.Visible) s.ZeroRpm = ZeroRpmToggle.IsOn;
            if (t.FanCurveSupported && _fan.Count > 0) s.FanCurve = _fan.Select(p => p.Clone()).ToList();
            if (FanMinRpmBox.Visibility == Visibility.Visible && !double.IsNaN(FanMinRpmBox.Value)) s.FanMinRpm = (int)FanMinRpmBox.Value;
            if (FanTargetRpmBox.Visibility == Visibility.Visible && !double.IsNaN(FanTargetRpmBox.Value)) s.FanTargetRpm = (int)FanTargetRpmBox.Value;
        }

        return s;
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        LoadAll();
        StatusBar.IsOpen = false;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e) => await ApplyAsync(skipConfirm: false);

    private async Task ApplyAsync(bool skipConfirm)
    {
        if (_busy || _loading) return;
        if (!skipConfirm && App.Settings.Current.ConfirmTuning)
        {
            var r = await ConfirmAsync(XamlRoot, L("Tuning.ConfirmTitle"), L("Tuning.ConfirmText"), L("Common.Apply"), L("Common.Cancel"));
            if (r != ContentDialogResult.Primary) return;
        }

        _busy = true;
        try
        {
            var settings = Collect();
            ApplyReport report;
            if (skipConfirm)
            {
                report = await Task.Run(() => App.Hardware.ApplyTuning(settings));
            }
            else
            {
                var (rep, kept) = await ApplyTuningSafeAsync(XamlRoot, settings);
                report = rep;
                if (!kept)
                {
                    Show(StatusBar, InfoBarSeverity.Warning, L("Safe.Reverted"));
                    LoadAll();
                    return;
                }
            }

            ShowReport(StatusBar, report);
            if (!skipConfirm)
            {
                LoadAll();
            }
            else
            {
                PendingText.Text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"Tuning: {ex.Message}");
            Show(StatusBar, InfoBarSeverity.Error, L("Msg.ApplyFailed"), ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void Factory_Click(object sender, RoutedEventArgs e)
    {
        var r = await ConfirmAsync(XamlRoot, L("Tuning.FactoryTitle"), L("Tuning.FactoryText"), L("Common.Yes"), L("Common.Cancel"));
        if (r != ContentDialogResult.Primary) return;

        var res = await Task.Run(() => App.Hardware.ResetTuningToFactory());
        if (res.Succeeded()) Show(StatusBar, InfoBarSeverity.Success, L("Tuning.FactoryDone"));
        else Show(StatusBar, InfoBarSeverity.Error, L("Msg.ApplyFailed"), res.Describe());
        LoadAll();
    }
}
