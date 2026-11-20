using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RedControlUtility.Models;
using RedControlUtility.Services;
using RedControlUtility.Services.Adlx;
using static RedControlUtility.UiHelper;

namespace RedControlUtility.Views;

public sealed partial class DashboardPage : Page
{
    private readonly List<StatTile> _all = new()
    {
        new StatTile { TitleKey = "Stat.GpuUsage", TipKey = "StatTip.GpuUsage", Unit = "%" },
        new StatTile { TitleKey = "Stat.GpuClock", TipKey = "StatTip.GpuClock", Unit = "MHz" },
        new StatTile { TitleKey = "Stat.VramClock", TipKey = "StatTip.VramClock", Unit = "MHz", Optional = true },
        new StatTile { TitleKey = "Stat.TempEdge", TipKey = "StatTip.TempEdge", Unit = "°C", Optional = true },
        new StatTile { TitleKey = "Stat.TempHotspot", TipKey = "StatTip.TempHotspot", Unit = "°C", Optional = true },
        new StatTile { TitleKey = "Stat.TempMemory", TipKey = "StatTip.TempMemory", Unit = "°C", Optional = true },
        new StatTile { TitleKey = "Stat.Power", TipKey = "StatTip.Power", Unit = "W", Optional = true },
        new StatTile { TitleKey = "Stat.Fan", TipKey = "StatTip.Fan", Unit = "RPM", Optional = true },
        new StatTile { TitleKey = "Stat.VramUsed", TipKey = "StatTip.VramUsed", Unit = "MB", Optional = true },
        new StatTile { TitleKey = "Stat.Voltage", TipKey = "StatTip.Voltage", Unit = "mV" },
    };

    private readonly ObservableCollection<StatTile> _tiles = new();

    public DashboardPage()
    {
        InitializeComponent();
        foreach (var t in _all) _tiles.Add(t);
        TilesView.ItemsSource = _tiles;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Sensors.Updated += OnSensors;
        App.Sensors.Start(App.Settings.Current.SensorIntervalMs);
        App.Engine.ActiveChanged += OnActiveChanged;
        App.Localization.LanguageChanged += RefreshAll;
        RefreshAll();
        UpdateTiles(App.Sensors.Latest);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.Sensors.Updated -= OnSensors;
        App.Sensors.Stop();
        App.Engine.ActiveChanged -= OnActiveChanged;
        App.Localization.LanguageChanged -= RefreshAll;
    }

    private void OnSensors(MetricsSnapshot s) => DispatcherQueue.TryEnqueue(() => UpdateTiles(s));
    private void OnActiveChanged(AppProfile? p) => DispatcherQueue.TryEnqueue(UpdateActive);

    private void RefreshAll()
    {
        var hw = App.Hardware;
        var gpu = hw.CurrentGpu;

        GpuNameText.Text = gpu.Name;
        GpuArchText.Text = gpu.Architecture == GpuArchitecture.Unknown ? L("Dashboard.ArchUnknown") : gpu.ArchitectureText;

        InfoGrid.Children.Clear();
        InfoGrid.RowDefinitions.Clear();
        int row = 0;
        void Add(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            InfoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = new TextBlock { Text = L(key), Opacity = 0.7 };
            var v = new TextBlock { Text = value, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
            ToolTipService.SetToolTip(k, L(key.Replace("Info.", "InfoTip.")));
            ToolTipService.SetToolTip(v, L(key.Replace("Info.", "InfoTip.")));
            Grid.SetRow(k, row); Grid.SetColumn(k, 0);
            Grid.SetRow(v, row); Grid.SetColumn(v, 1);
            InfoGrid.Children.Add(k);
            InfoGrid.Children.Add(v);
            row++;
        }

        Add("Info.DeviceId", string.IsNullOrEmpty(gpu.DeviceId) ? string.Empty : $"{gpu.VendorId}:{gpu.DeviceId}" + (string.IsNullOrEmpty(gpu.RevisionId) ? string.Empty : $" (rev {gpu.RevisionId})"));
        Add("Info.Vram", gpu.VramMb > 0 ? $"{gpu.VramMb} MB {gpu.VramType}".Trim() : string.Empty);
        Add("Info.Type", gpu.FromAdlx ? (gpu.IsDiscrete ? L("Info.Discrete") : L("Info.Integrated")) : string.Empty);
        Add("Info.Driver", gpu.DriverVersion);
        Add("Info.Bios", gpu.BiosVersion);
        Add("Info.Backend", hw.AdlxAvailable ? $"{hw.BackendName}  (ADLX {hw.AdlxVersion})" : hw.BackendName);
        if (hw.Caps.SamSupported) Add("Info.Sam", hw.Caps.SamEnabled ? L("Common.On") : L("Common.Off"));

        if (!hw.AdlxAvailable)
        {
            Show(StatusBar, InfoBarSeverity.Warning, L("Dashboard.NoAdlxTitle"), L("Dashboard.NoAdlxMsg"));
        }
        else
        {
            StatusBar.IsOpen = false;
        }

        PresetCard.Visibility = hw.AdlxAvailable && hw.Caps.Tuning.AnyTuning ? Visibility.Visible : Visibility.Collapsed;
        foreach (var t in _all) { t.Title = L(t.TitleKey); t.Tip = L(t.TipKey); }
        DiagBox.Text = hw.BuildDiagnostics();
        UpdateActive();
    }

    private void UpdateActive()
    {
        var a = App.Engine.Active;
        ActiveAppText.Text = a != null
            ? L("Dashboard.ActiveAppOn", a.Name)
            : (App.Settings.Current.PerAppProfilesEnabled ? L("Dashboard.ActiveAppNone") : L("Dashboard.ActiveAppDisabled"));
    }

    private int? _lastVolt;
    private readonly bool[] _seen = new bool[16];

    private void UpdateTiles(MetricsSnapshot s)
    {
        static string N(double? v, string f = "0") => v.HasValue ? v.Value.ToString(f) : "—";
        static string I(int? v) => v.HasValue ? v.Value.ToString() : "—";
        static double F(double? v, double max) => v.HasValue ? Math.Clamp(v.Value / max * 100, 0, 100) : 0;

        void Set(int i, string value, double fraction)
        {
            _all[i].Value = value;
            _all[i].Fraction = fraction;
        }

        double? Pos(double? v) => v.HasValue && v.Value > 0 ? v : null;
        int? PosI(int? v) => v.HasValue && v.Value > 0 ? v : null;
        double? mem = Pos(s.TempMemoryC), hot = Pos(s.TempHotspotC), edge = Pos(s.TempEdgeC);
        double? pw = Pos(s.BoardPowerW ?? s.PowerW);
        // A tensão é informação vital: nunca some. Leituras vazias ou zeradas mantêm o último valor válido.
        int? voltNow = PosI(s.VoltageMv);
        if (voltNow.HasValue) _lastVolt = voltNow;
        int? fan = s.FanRpm, vclk = PosI(s.VramClockMhz), volt = voltNow ?? _lastVolt, vused = s.VramUsedMb;

        Set(0, N(s.GpuUsage), F(s.GpuUsage, 100));
        Set(1, I(s.GpuClockMhz), F(s.GpuClockMhz, 3500));
        Set(2, I(vclk), F(vclk, 2500));
        Set(3, N(edge), F(edge, 110));
        Set(4, N(hot), F(hot, 110));
        Set(5, N(mem), F(mem, 110));
        Set(6, N(pw), F(pw, 400));
        Set(7, I(fan), F(fan, 4000));
        Set(8, I(vused), F(vused, Math.Max(1, App.Hardware.CurrentGpu.VramMb)));
        Set(9, I(volt), F(volt, 1300));

        // Sensores que esta GPU não tem somem do painel (só depois da primeira leitura real).
        bool real = s.GpuUsage.HasValue || s.GpuClockMhz.HasValue || s.TempEdgeC.HasValue;
        if (!real) return;
        bool[] has = { true, true, vclk.HasValue, edge.HasValue, hot.HasValue, mem.HasValue, pw.HasValue, fan.HasValue, vused.HasValue, volt.HasValue };
        for (int i = 0; i < _all.Count; i++)
        {
            // Sensor que já apareceu uma vez não volta a sumir por uma leitura vazia isolada.
            _seen[i] |= has[i];
            _all[i].HasValue = _seen[i];
        }

        var want = _all.Where(t => !t.Optional || t.HasValue).ToList();
        if (!want.SequenceEqual(_tiles))
        {
            _tiles.Clear();
            foreach (var t in want) _tiles.Add(t);
        }
    }

    private async void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        var hw = App.Hardware;
        try
        {
            if (key == "Balanced")
            {
                var r = await Task.Run(() => hw.ResetTuningToFactory());
                if (r.Succeeded()) Show(StatusBar, InfoBarSeverity.Success, L("Tuning.FactoryDone"));
                else Show(StatusBar, InfoBarSeverity.Error, L("Msg.ApplyFailed"), r.Describe());
                return;
            }

            var current = await Task.Run(() => hw.ReadTuning());
            var settings = PresetService.Build(key, hw.Caps.Tuning, current, hw.TuningBaseline);
            if (settings == null)
            {
                Show(StatusBar, InfoBarSeverity.Warning, L("Presets.Unsupported"));
                return;
            }

            var (report, kept) = await ApplyTuningSafeAsync(XamlRoot, settings);
            if (!kept) Show(StatusBar, InfoBarSeverity.Warning, L("Safe.Reverted"));
            else ShowReport(StatusBar, report);
        }
        catch (Exception ex)
        {
            LogService.Error($"Preset {key}: {ex.Message}");
            Show(StatusBar, InfoBarSeverity.Error, L("Msg.ApplyFailed"), ex.Message);
        }
    }

    private void CopyDiag_Click(object sender, RoutedEventArgs e)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(App.Hardware.BuildDiagnostics());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        Show(StatusBar, InfoBarSeverity.Success, L("Dashboard.DiagCopied"));
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = AppPaths.LogDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Error($"Abrir logs: {ex.Message}");
        }
    }
}
