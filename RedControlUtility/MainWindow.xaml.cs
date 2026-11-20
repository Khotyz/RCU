using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using RedControlUtility.Models;
using RedControlUtility.Services;
using RedControlUtility.Views;

namespace RedControlUtility;

public sealed partial class MainWindow : Window
{
    private readonly TrayService _tray = new();
    private bool _exiting;
    private Type? _lastPage;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SizeToScreen();
        Title = "Red Control Utility";
        NavView.SelectedItem = NavDashboardItem;
        UpdateNavMode();
        App.ModeChanged += () => DispatcherQueue.TryEnqueue(UpdateNavMode);
        AppWindow.Changed += AppWindow_Changed;
        UpdateService.Found += info => DispatcherQueue.TryEnqueue(() => ShowUpdateAvailable(info));
        SetupTray();

        NavView.Loaded += async (_, _) =>
        {
            if (App.RecoveredFromCrash)
            {
                await UiHelper.ConfirmAsync(NavView.XamlRoot, UiHelper.L("Recovery.Title"), UiHelper.L("Recovery.Text"), string.Empty, UiHelper.L("Common.Ok"));
            }
        };
    }

    private const int MinWidthDip = 900;
    private const int MinHeightDip = 600;
    private int _minWidthPx;
    private int _minHeightPx;

    private bool _applyingSize;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private uint CurrentDpi()
    {
        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi >= 96) return dpi;
        }
        catch
        {
        }

        return 96;
    }

    private void ComputeMinSize(uint dpi)
    {
        double scale = dpi / 96.0;
        _minWidthPx = (int)Math.Round(MinWidthDip * scale);
        _minHeightPx = (int)Math.Round(MinHeightDip * scale);
        try
        {
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            _minWidthPx = Math.Min(_minWidthPx, work.Width);
            _minHeightPx = Math.Min(_minHeightPx, work.Height);
        }
        catch
        {
        }
    }

    private void SizeToScreen()
    {
        try
        {
            uint dpi = CurrentDpi();
            ComputeMinSize(dpi);
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            int w = Math.Min(work.Width, Math.Max(_minWidthPx, (int)Math.Round(work.Width * 0.92)));
            int h = Math.Min(work.Height, Math.Max(_minHeightPx, (int)Math.Round(work.Height * 0.92)));
            _applyingSize = true;
            try
            {
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
            }
            finally
            {
                _applyingSize = false;
            }
        }
        catch
        {
        }
    }

    private void ClampMinSize()
    {
        if (_applyingSize) return;
        try
        {
            ComputeMinSize(CurrentDpi());
            var size = AppWindow.Size;
            int w = Math.Max(size.Width, _minWidthPx);
            int h = Math.Max(size.Height, _minHeightPx);
            if (w == size.Width && h == size.Height) return;

            _applyingSize = true;
            try
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
            }
            finally
            {
                _applyingSize = false;
            }
        }
        catch
        {
        }
    }

    private void OnWindowSizeChanged()
    {
        if (_applyingSize) return;
        ClampMinSize();
    }

    private void UpdateNavMode()
    {
        var vis = App.Settings.Current.AdvancedMode ? Visibility.Visible : Visibility.Collapsed;
        NavDisplayItem.Visibility = vis;
        NavProfilesItem.Visibility = vis;
        if (vis == Visibility.Collapsed && NavView.SelectedItem is NavigationViewItem { Tag: "Display" or "Profiles" })
        {
            NavView.SelectedItem = NavDashboardItem;
        }
    }

    private void SetupTray()
    {
        try
        {
            string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "AppIcon.ico");
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (System.IO.File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }

            IntPtr hIcon = TrayService.LoadIcon(iconPath);
            _tray.Initialize(hwnd, hIcon, "Red Control Utility");
            _tray.OpenRequested += RestoreFromTray;
            _tray.Minimized += () => DispatcherQueue.TryEnqueue(HideToTray);
            _tray.ExitRequested += () =>
            {
                _exiting = true;
                _tray.Dispose();
                Close();
            };
            Closed += (_, _) =>
            {
                App.Engine.ActiveChanged -= OnEngineActiveChanged;
                _tray.Dispose();
                OverlayService.Shared.Dispose();
            };

            _tray.Show();

            App.Engine.ActiveChanged += OnEngineActiveChanged;
        }
        catch (Exception ex)
        {
            LogService.Error($"Bandeja: {ex.Message}");
        }
    }

    private void OnEngineActiveChanged(AppProfile? p)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (p == null || !App.Settings.Current.NotifyOnProfile) return;
            try
            {
                var items = ProfileSummary.Build(p, App.Hardware.Caps.Graphics,
                    App.Hardware.CurrentGpu.Architecture.SupportsFsr4() && (App.Hardware.Caps.Graphics.Fsr || Services.Adl.DriverFsrService.IsSupported()));
                OverlayService.Shared.Show(UiHelper.L("Notify.Title", string.IsNullOrWhiteSpace(p.Name) ? p.ExeName : p.Name), items, App.Settings.Current.OverlaySeconds, App.Settings.Current.OverlayCorner);
            }
            catch (Exception ex) { LogService.Error($"Overlay: {ex.Message}"); }
        });
    }

    private void RestoreFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter p) p.Restore();
        if (ContentFrame.Content == null && _lastPage != null) ContentFrame.Navigate(_lastPage);
        Activate();
        if (ContentFrame.Content is DashboardPage && !App.Sensors.Running) App.Sensors.Start(App.Settings.Current.SensorIntervalMs);
    }

    private void HideToTray()
    {
        if (_exiting) return;
        _tray.Show();
        AppWindow.Hide();
        App.Sensors.Stop();

        _lastPage = ContentFrame.CurrentSourcePageType ?? _lastPage;
        ContentFrame.Content = null;
        ContentFrame.BackStack.Clear();
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(2);
        t.IsRepeating = false;
        t.Tick += (_, _) => MemoryService.Trim();
        t.Start();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            OnWindowSizeChanged();
        }

        if (args.DidVisibilityChange && sender.IsVisible && ContentFrame.Content is DashboardPage && !App.Sensors.Running)
        {
            App.Sensors.Start(App.Settings.Current.SensorIntervalMs);
        }
    }

    private void ShowUpdateAvailable(UpdateInfo info)
    {
        try
        {
            UpdateBar.Title = UiHelper.L("Update.Available", info.Version.ToString(3));
            UpdateBar.Message = UiHelper.L("Update.Message", UpdateService.CurrentText);
            UpdateBar.IsOpen = true;
        }
        catch
        {
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var info = UpdateService.Available;
        if (info == null)
        {
            UpdateBar.IsOpen = false;
            return;
        }

        UpdateButton.IsEnabled = false;
        try
        {
            var (ok, message) = await UpdateService.InstallAsync(info);
            UpdateBar.Message = ok ? UiHelper.L("Update.Started") : UiHelper.L("Update.Failed");
            UpdateBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            if (!ok) LogService.Warn($"Atualização: {message}");
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            Type pageType = tag switch
            {
                "Dashboard" => typeof(DashboardPage),
                "Display" => typeof(DisplayPage),
                "Apps" => typeof(AppsPage),
                "Tuning" => typeof(TuningPage),
                "Profiles" => typeof(ProfilesPage),
                "Settings" => typeof(SettingsPage),
                _ => typeof(DashboardPage)
            };
            ContentFrame.Navigate(pageType);
        }
    }
}
