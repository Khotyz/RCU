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

        // O WinUI ajusta a janela ao conteúdo no primeiro layout: reaplica o alvo depois dele.
        NavView.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ReapplyTarget);

        // Vigia: qualquer crescimento que passe pelo AppWindow.Changed é desfeito aqui também.
        _sizeWatch = DispatcherQueue.CreateTimer();
        _sizeWatch.Interval = TimeSpan.FromMilliseconds(500);
        _sizeWatch.IsRepeating = true;
        _sizeWatch.Tick += (_, _) =>
        {
            if (_applyingSize || _target.Width <= 0 || IsMaxOrMinimized()) return;
            var s = AppWindow.Size;
            if (s.Width <= _target.Width + 2 && s.Height <= _target.Height + 2) return;
            if (GetAsyncKeyState(VkLButton) < 0 || GetCapture() != IntPtr.Zero) return; // usuário arrastando
            if (!AllowCorrection()) return;
            LogService.Info($"Janela vigia: {s.Width}x{s.Height} > alvo {_target.Width}x{_target.Height} - voltando ao alvo.");
            ReapplyTarget();
        };
        _sizeWatch.Start();

        NavView.Loaded += async (_, _) =>
        {
            if (App.RecoveredFromCrash)
            {
                await UiHelper.ConfirmAsync(NavView.XamlRoot, UiHelper.L("Recovery.Title"), UiHelper.L("Recovery.Text"), string.Empty, UiHelper.L("Common.Ok"));
            }
        };
    }

    // Tamanho mínimo em unidades de layout (DIP), não em pixels: em telas com escala 125%/150% o
    // tamanho em pixels precisa ser multiplicado, senão a janela nasce "pequena".
    private const int MinWidthDip = 1280;
    private const int MinHeightDip = 820;
    private const int VkLButton = 0x01;
    private int _minWidthPx;
    private int _minHeightPx;

    // Tamanho alvo da janela. Qualquer mudança de tamanho que não venha do usuário volta para cá: o WinUI
    // reajusta a janela ao conteúdo quando a página pede mais espaço, e era isso que fazia a janela
    // "expandir" ao ligar uma opção na aba de ajuste fino.
    private Windows.Graphics.RectInt32 _target;
    private Windows.Graphics.PointInt32 _lastPos;
    private uint _targetDpi;
    private bool _applyingSize;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sizeWatch;
    private bool _userSized;
    private int _corrections;
    private long _correctionWindow;
    private readonly long _startedAt = Environment.TickCount64;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    /// <summary>Janela maximizada ou minimizada: o tamanho é do Windows, não do app.</summary>
    private bool IsMaxOrMinimized()
    {
        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            return IsZoomed(hwnd) || IsIconic(hwnd);
        }
        catch
        {
            return false;
        }
    }

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

    /// <summary>Calcula o tamanho alvo: 92% da área útil, nunca menor que o mínimo, centralizado.</summary>
    private void ComputeTarget(uint dpi)
    {
        _targetDpi = dpi;
        double scale = dpi / 96.0;
        _minWidthPx = (int)Math.Round(MinWidthDip * scale);
        _minHeightPx = (int)Math.Round(MinHeightDip * scale);

        try
        {
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

            // Em telas pequenas o mínimo não pode passar da área útil.
            _minWidthPx = Math.Min(_minWidthPx, work.Width);
            _minHeightPx = Math.Min(_minHeightPx, work.Height);

            int w = Math.Min(work.Width, Math.Max(_minWidthPx, (int)Math.Round(work.Width * 0.92)));
            int h = Math.Min(work.Height, Math.Max(_minHeightPx, (int)Math.Round(work.Height * 0.92)));
            _target = new Windows.Graphics.RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h);
        }
        catch
        {
            _target = new Windows.Graphics.RectInt32(0, 0, Math.Max(_minWidthPx, 1024), Math.Max(_minHeightPx, 700));
        }
    }

    /// <summary>Posiciona a janela no tamanho alvo (antes de a janela aparecer).</summary>
    private void SizeToScreen()
    {
        try
        {
            ComputeTarget(CurrentDpi());
            _applyingSize = true;
            try
            {
                AppWindow.MoveAndResize(_target);
            }
            finally
            {
                _applyingSize = false;
            }

            _lastPos = AppWindow.Position;
        }
        catch
        {
        }
    }

    /// <summary>
    /// Limita as correções a cada 2 s (o vigia e o evento de tamanho usam o mesmo orçamento). Se o WinUI insistir em reajustar a janela ao conteúdo, o app para
    /// de brigar (melhor uma janela alguns pixels maior do que uma animação em laço).
    /// </summary>
    private bool AllowCorrection()
    {
        long now = Environment.TickCount64;
        if (now - _correctionWindow > 2000)
        {
            _correctionWindow = now;
            _corrections = 0;
        }

        return ++_corrections <= 6;
    }

    private void ReapplyTarget()
    {
        if (_target.Width <= 0 || _applyingSize) return;
        if (IsMaxOrMinimized()) return;
        try
        {
            _applyingSize = true;
            try
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(_target.Width, _target.Height));
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

    /// <summary>Mantém a janela no tamanho mínimo, sem nunca crescer por conta própria.</summary>
    private void ClampMinSize()
    {
        if (_minWidthPx <= 0 || _applyingSize) return;
        try
        {
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

    /// <summary>
    /// A janela mudou de tamanho. Crescer sem o usuário pedir é sempre desfeito (é o WinUI reajustando a
    /// janela ao conteúdo da página, o que fazia a janela "expandir" ao ligar opções no ajuste fino).
    /// Encolher só é desfeito nos primeiros segundos, antes de o usuário tocar na janela.
    /// </summary>
    private void OnWindowSizeChanged()
    {
        if (_applyingSize) return;

        try
        {
            // Mudou de monitor/escala: o alvo é recalculado, não é "crescimento".
            uint dpi = CurrentDpi();
            if (_targetDpi != 0 && dpi != _targetDpi)
            {
                ComputeTarget(dpi);
                ReapplyTarget();
                _lastPos = AppWindow.Position;
                return;
            }

            if (_target.Width <= 0) return;

            var pos = AppWindow.Position;
            bool moved = pos.X != _lastPos.X || pos.Y != _lastPos.Y;
            _lastPos = pos;

            bool dragging = GetAsyncKeyState(VkLButton) < 0 || GetCapture() != IntPtr.Zero;
            if (dragging)
            {
                ClampMinSize();
                return;
            }

            var size = AppWindow.Size;
            bool grew = size.Width > _target.Width + 2 || size.Height > _target.Height + 2;
            bool shrank = size.Width < _target.Width - 2 || size.Height < _target.Height - 2;

            // Encaixe/redimensionamento feito pelo usuário (mexeu na posição e diminuiu): vale o dele.
            if (moved && shrank)
            {
                _userSized = true;
                ClampMinSize();
                return;
            }

            bool shrankAtStart = !_userSized && Environment.TickCount64 - _startedAt < 8000;

            if ((grew || (shrank && shrankAtStart)) && AllowCorrection())
            {
                LogService.Info($"Janela: {size.Width}x{size.Height} fora do alvo {_target.Width}x{_target.Height} (cresceu={grew}, moveu={moved}) - voltando ao alvo.");
                ReapplyTarget();
                return;
            }

            ClampMinSize();
        }
        catch (Exception ex)
        {
            LogService.Error($"Janela: {ex.Message}");
        }
    }

    // Modo Simples esconde Vídeo e Perfis (o essencial fica em Painel, Gráficos, Apps, Tuning e Configurações).
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

            // O ícone fica sempre na bandeja enquanto o app estiver aberto.
            _tray.Show();

            // Overlay minimalista com as tecnologias ativas quando um perfil de jogo entra em ação.
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

    // Janela minimizada: some da barra de tarefas, para os sensores e devolve memória ao sistema.
    private void HideToTray()
    {
        if (_exiting) return;
        _tray.Show();
        AppWindow.Hide();
        App.Sensors.Stop();

        // Libera a página atual (árvore visual XAML) enquanto estiver na bandeja; ela é recriada ao restaurar.
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

    /// <summary>Barra sobreposta com a versão nova encontrada no GitHub.</summary>
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
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ReapplyTarget);
        }
    }
}
