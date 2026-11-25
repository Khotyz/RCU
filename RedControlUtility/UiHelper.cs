using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RedControlUtility.Models;
using RedControlUtility.Services;
using RedControlUtility.Services.Adlx;

namespace RedControlUtility;

public static class UiHelper
{
    private static long _lastTuningApplyTicks;
    public static DateTime LastTuningApplyUtc
    {
        get => new(Interlocked.Read(ref _lastTuningApplyTicks), DateTimeKind.Utc);
        set => Interlocked.Exchange(ref _lastTuningApplyTicks, value.Ticks);
    }

    public static void PaintTip(ToolTip tip)
    {
        var c = ThemeService.Colors;
        tip.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(c.Surface);
        tip.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(c.Text);
        tip.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(c.ControlBorder);
        tip.BorderThickness = new Thickness(1);
    }

    public static void SetTip(DependencyObject target, string text)
    {
        var tip = new ToolTip { Content = text };
        PaintTip(tip);
        tip.Opened += (s, _) => PaintTip((ToolTip)s);
        ToolTipService.SetToolTip(target, tip);
    }

    public static string L(string key) => App.Localization.Get(key);
    public static string L(string key, params object[] args) => App.Localization.Get(key, args);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RedControlUtility.Controls.Banner, DispatcherQueueTimer> BarTimers = new();

    public static void Show(RedControlUtility.Controls.Banner bar, InfoBarSeverity severity, string title, string message = "")
    {
        bool same = bar.IsOpen && bar.Severity == severity && bar.Title == title && bar.Message == message;
        if (!same)
        {
            bar.Severity = severity;
            bar.Title = title;
            bar.Message = message;
            if (!bar.IsOpen) bar.IsOpen = true;
        }

        try
        {
            if (!BarTimers.TryGetValue(bar, out var timer))
            {
                timer = bar.DispatcherQueue.CreateTimer();
                timer.IsRepeating = false;
                timer.Tick += (t, _) =>
                {
                    t.Stop();
                    bar.IsOpen = false;
                };
                BarTimers.Add(bar, timer);
            }

            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(severity is InfoBarSeverity.Error or InfoBarSeverity.Warning ? 14 : 5);
            timer.Start();
        }
        catch
        {
        }
    }

    public static void ShowReport(RedControlUtility.Controls.Banner bar, ApplyReport report)
    {
        if (report.Empty)
        {
            Show(bar, InfoBarSeverity.Informational, L("Msg.NothingToApply"));
        }
        else if (report.AllOk)
        {
            Show(bar, InfoBarSeverity.Success, L("Msg.Applied"), report.Detail);
        }
        else if (report.AnyOk)
        {
            Show(bar, InfoBarSeverity.Warning, L("Msg.AppliedPartial"), report.Detail);
        }
        else
        {
            Show(bar, InfoBarSeverity.Error, L("Msg.ApplyFailed"), report.Detail);
        }
    }

    public static async Task<ContentDialogResult> ConfirmAsync(XamlRoot root, string title, string message, string primary, string close)
    {
        var dlg = new ContentDialog
        { RequestedTheme = ThemeService.Element,
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Close
        };
        return await ThemeService.ShowAsync(dlg);
    }

    public static async Task RunUpdateAsync(XamlRoot root, UpdateInfo info)
    {
        var lines = new StackPanel { Spacing = 8 };
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Margin = new Thickness(0, 4, 0, 0) };
        var body = new StackPanel { Spacing = 12, MinWidth = 380 };
        body.Children.Add(lines);
        body.Children.Add(bar);

        var dlg = new ContentDialog
        {
            RequestedTheme = ThemeService.Element,
            XamlRoot = root,
            Title = L("Update.Title", info.Version.ToString(3)),
            Content = body
        };

        TextBlock? current = null;
        bool closing = false;
        TextBlock Line(string text)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 15 };
            lines.Children.Add(t);
            return t;
        }

        static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0");

        var progress = new Progress<UpdateProgress>(p =>
        {
            switch (p.Stage)
            {
                case UpdateService.StageDownload:
                    current ??= Line(string.Empty);
                    current.Text = "… " + L("Update.Step.Download", p.Percent, Mb(p.Bytes), Mb(p.Total));
                    bar.Value = p.Percent;
                    break;
                case UpdateService.StageDownloaded:
                    if (current != null) current.Text = "✓ " + L("Update.Step.Downloaded");
                    current = null;
                    bar.Value = 100;
                    break;
                case UpdateService.StageLaunch:
                    current = Line("… " + L("Update.Step.Install"));
                    bar.IsIndeterminate = true;
                    break;
                case UpdateService.StageLaunched:
                    if (current != null) current.Text = "✓ " + L("Update.Step.Installed");
                    current = null;
                    break;
                case UpdateService.StageClosing:
                    closing = true;
                    current = Line("… " + L("Update.Step.Closing"));
                    break;
                case UpdateService.StagePage:
                    current = Line("… " + L("Update.Step.OpenPage"));
                    bar.IsIndeterminate = true;
                    break;
            }
        });

        var shown = ThemeService.ShowAsync(dlg).AsTask();
        var (ok, message) = await UpdateService.InstallAsync(info, progress);
        bar.IsIndeterminate = false;
        if (!ok)
        {
            bar.Visibility = Visibility.Collapsed;
            Line("✗ " + L("Update.Step.Failed", message));
        }
        else if (!closing)
        {
            bar.Value = 100;
            Line("✓ " + L("Update.Step.Done"));
        }

        if (!ok || !closing)
        {
            dlg.CloseButtonText = L("Common.Ok");
            await shown;
        }
    }

    public static async Task<bool> ConfirmKeepAsync(XamlRoot root, int seconds)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        int left = seconds;
        void Update() => text.Text = L("Safe.Text", left);
        Update();
        var dlg = new ContentDialog
        { RequestedTheme = ThemeService.Element,
            XamlRoot = root,
            Title = L("Safe.Title"),
            Content = text,
            PrimaryButtonText = L("Safe.Keep"),
            CloseButtonText = L("Safe.Revert"),
            DefaultButton = ContentDialogButton.Close
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            left--;
            if (left <= 0) { timer.Stop(); dlg.Hide(); }
            else Update();
        };
        timer.Start();
        var r = await ThemeService.ShowAsync(dlg);
        timer.Stop();
        return r == ContentDialogResult.Primary;
    }

    public static async Task<(ApplyReport Report, bool Kept)> ApplyTuningSafeAsync(XamlRoot root, TuningSettings settings)
    {
        var hw = App.Hardware;
        var before = await Task.Run(() => hw.ReadTuning());
        try { File.WriteAllText(AppPaths.PendingFlag, DateTime.Now.ToString("o")); } catch { }

        UiHelper.LastTuningApplyUtc = DateTime.UtcNow;
        var report = await Task.Run(() => hw.ApplyTuning(settings));
        UiHelper.LastTuningApplyUtc = DateTime.UtcNow;
        bool kept = true;
        if (report.AnyOk && App.Settings.Current.AutoRevertTuning)
        {
            kept = await ConfirmKeepAsync(root, 15);
            if (!kept)
            {
                var back = await Task.Run(() => hw.ApplyTuning(before));
                LogService.Info($"Ajuste revertido: {back.Summary}");
                LastTuningApplyUtc = DateTime.UtcNow;
            }
        }

        try { File.Delete(AppPaths.PendingFlag); } catch { }
        if (kept && report.AnyOk) TuningPersistenceService.Remember(settings);
        return (report, kept);
    }

    public static ComboBoxItem Item(string text, object? tag = null) => new() { Content = text, Tag = tag };

    public static bool? TriToBool(int selectedIndex) => selectedIndex switch { 1 => true, 2 => false, _ => null };
    public static int BoolToTri(bool? v) => v switch { true => 1, false => 2, _ => 0 };
}

public sealed class Debouncer
{
    private readonly DispatcherQueueTimer _timer;

    public Debouncer(DispatcherQueue queue, int milliseconds, Action action)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => action();
    }

    public void Trigger()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel() => _timer.Stop();
}
