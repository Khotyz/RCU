using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using RedControlUtility.Models;
using RedControlUtility.Services;
using RedControlUtility.Services.Adlx;

namespace RedControlUtility;

public partial class App : Application
{
    public static SettingsService Settings { get; } = new();
    public static HardwareService Hardware { get; } = new();
    public static DisplayColorService DisplayColor { get; } = new();
    public static ProfileManager Profiles { get; } = new();
    public static LocalizationService Localization { get; } = new();
    public static SensorService Sensors { get; private set; } = null!;
    public static AppProfileEngine Engine { get; private set; } = null!;
    public static StabilityWatchdog Stability { get; } = new();
    public static DisplayPersistenceService DisplayPersistence { get; } = new();
    public static OverheatMonitor Overheat { get; } = new();
    public static UtilityProfile CurrentProfile { get; set; } = new();
    public static Window? MainWindowInstance { get; private set; }
    public static bool RecoveredFromCrash { get; private set; }
    public static event Action? ModeChanged;
    public static void RaiseModeChanged() => ModeChanged?.Invoke();

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            LogService.Error($"Exceção não tratada: {e.Exception}");
            e.Handled = true;
        };

        Settings.Load();
        var s = Settings.Current;
        Localization.LoadLanguage(string.IsNullOrEmpty(s.Language) ? LocalizationService.DetectSystemLanguage() : s.Language);

        Hardware.Initialize(s.Backend, s.GpuIndex);
        Sensors = new SensorService(Hardware);

        CurrentProfile = Profiles.LoadProfile(AppPaths.ProfileFile) ?? new UtilityProfile();
        CurrentProfile.GpuModel = Hardware.CurrentGpu.Name;
        CurrentProfile.Architecture = Hardware.CurrentGpu.Architecture;

        Engine = new AppProfileEngine(Hardware, () => CurrentProfile.Apps, () => CurrentProfile.GlobalGame);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] cmd = Environment.GetCommandLineArgs();

        RecoveredFromCrash = File.Exists(AppPaths.PendingFlag);
        if (RecoveredFromCrash) LogService.Warn("Marcador de ajuste pendente encontrado: pulando aplicação automática.");

        int applyIdx = Array.FindIndex(cmd, a => string.Equals(a, "--apply", StringComparison.OrdinalIgnoreCase));
        if (applyIdx >= 0 && applyIdx + 1 < cmd.Length)
        {
            var profile = await Profiles.LoadProfileAsync(cmd[applyIdx + 1]);
            if (profile != null && !RecoveredFromCrash)
            {
                var report = ApplyProfile(profile);
                LogService.Info($"--apply: {report.Summary}");
            }

            Hardware.Dispose();
            Application.Current.Exit();
            return;
        }

        int launchIdx = Array.FindIndex(cmd, a => string.Equals(a, "--launch", StringComparison.OrdinalIgnoreCase));
        if (launchIdx >= 0 && launchIdx + 1 < cmd.Length)
        {
            await Task.Run(() => RunLaunchMode(cmd[launchIdx + 1], cmd.Skip(launchIdx + 2).ToArray()));
            Hardware.Dispose();
            Application.Current.Exit();
            return;
        }

        try { File.Delete(AppPaths.PendingFlag); } catch { }
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Closed += (_, _) =>
        {
            try
            {
                Stability.Stop();
                DisplayPersistence.Stop();
                Overheat.Stop();
                Engine.Stop();
                Sensors.Stop();
                SaveCurrentProfile();
                Settings.Save();
            }
            catch (Exception ex)
            {
                LogService.Error($"Encerramento: {ex.Message}");
            }
            finally
            {
                Hardware.Dispose();
            }
        };

        MainWindowInstance.Activate();

        bool minimized = cmd.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)) || Settings.Current.StartMinimized && cmd.Any(a => string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase));
        if (minimized)
        {
            if (Settings.Current.MinimizeToTray && MainWindowInstance is MainWindow mw) mw.HideAtStartup();
            else if (MainWindowInstance.AppWindow.Presenter is OverlappedPresenter p) p.Minimize();
        }

        RestartEngine();
        if (Settings.Current.GlobalFrtc && Hardware.AdlxAvailable) Hardware.ApplyGlobalFrtc(true, Settings.Current.GlobalFrtcFps);
        Stability.Unstable += OnGpuUnstable;
        Stability.Start();
        Overheat.Start();

        if (Settings.Current.CheckForUpdates)
        {
            _ = Task.Run(async () => await UpdateService.CheckAsync());
        }

        if (Settings.Current.ApplyProfileOnStartup && !RecoveredFromCrash)
        {
            var profile = CurrentProfile;
            var report = await Task.Run(() => ApplyProfile(profile));
            LogService.Info($"Aplicar ao iniciar: {report.Summary}");
        }

        _ = Task.Run(() => DisplayPersistence.Reapply(true));
        DisplayPersistence.Start();
    }

    private static void OnGpuUnstable(string what)
    {
        try
        {
            LogService.Warn($"Instabilidade da GPU detectada ({what}).");
            if (!Settings.Current.AutoResetOnInstability) return;
            bool hadTuning = CurrentProfile.Tuning.HasValues();
            if (!hadTuning) return;

            Hardware.Initialize(Settings.Current.Backend, Settings.Current.GpuIndex);
            var r = Hardware.ResetTuningToFactory();
            bool ok = r.Succeeded();
            LogService.Warn($"Reset automático do tuning: {r.Describe()}");

            CurrentProfile.Tuning = new TuningSettings();
            SaveCurrentProfile();

            MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var items = new List<OverlayItem> { new(UiHelper.L(ok ? "Notify.UnstableReset" : "Notify.UnstableKept"), what, ok) };
                    OverlayService.Shared.Show(UiHelper.L("Notify.Unstable"), items, Math.Max(6, Settings.Current.OverlaySeconds), Settings.Current.OverlayCorner);
                }
                catch (Exception ex)
                {
                    LogService.Error($"Overlay de instabilidade: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            LogService.Error($"OnGpuUnstable: {ex.Message}");
        }
    }

    public static void RestartEngine()
    {
        bool want = Settings.Current.PerAppProfilesEnabled && Hardware.AdlxAvailable && CurrentProfile.Apps.Any(a => a.Enabled);
        if (want && !Engine.IsRunning) Engine.Start();
        else if (!want && Engine.IsRunning) Engine.Stop();
    }

    public static ApplyReport ApplyProfile(UtilityProfile p)
    {
        var all = new ApplyReport();

        if (!p.Graphics.IsEmpty)
        {
            foreach (var i in Hardware.ApplyGraphics(p.Graphics).Items) all.Items.Add(i);
        }

        var t = p.Tuning;
        if (t.HasValues() && Hardware.Caps.Tuning.AnyTuning)
        {
            foreach (var i in Hardware.ApplyTuning(t).Items) all.Items.Add(i);
        }

        try
        {
            DisplayPersistenceService.ApplyColor(p);
        }
        catch (Exception ex)
        {
            LogService.Error($"Cor: {ex.Message}");
            all.Add("Color profile", AdlxResult.Fail);
        }

        return all;
    }

    public static UtilityProfile CaptureProfile()
    {
        var p = CurrentProfile;
        p.GpuModel = Hardware.CurrentGpu.Name;
        p.Architecture = Hardware.CurrentGpu.Architecture;
        if (Hardware.AdlxAvailable) p.Graphics = Hardware.ReadGraphics();
        if (Hardware.Caps.Tuning.AnyTuning) p.Tuning = Hardware.ReadTuning();

        return p;
    }

    public static void SaveCurrentProfile()
    {
        try
        {
            File.WriteAllText(AppPaths.ProfileFile, System.Text.Json.JsonSerializer.Serialize(CurrentProfile, Json.Options));
        }
        catch (Exception ex)
        {
            LogService.Error($"Salvar perfil: {ex.Message}");
        }
    }

    private static void RunLaunchMode(string exePath, string[] gameArgs)
    {
        try
        {
            string full = Path.GetFullPath(exePath);
            string name = Path.GetFileNameWithoutExtension(full);
            var profile = CurrentProfile.Apps.FirstOrDefault(a => a.Enabled &&
                (string.Equals(a.ExecutablePath, full, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(a.ExeName, Path.GetFileName(full), StringComparison.OrdinalIgnoreCase)));

            if (profile != null && Hardware.AdlxAvailable) Engine.ActivateNow(profile);
            else LogService.Info($"--launch: sem perfil/ADLX para {full}, apenas iniciando.");

            var psi = new System.Diagnostics.ProcessStartInfo(full)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(full) ?? string.Empty,
                Arguments = string.Join(' ', gameArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit();

            Thread.Sleep(5000);
            int misses = 0;
            while (misses < 5)
            {
                var running = System.Diagnostics.Process.GetProcessesByName(name);
                misses = running.Length == 0 ? misses + 1 : 0;
                foreach (var r in running) r.Dispose();
                Thread.Sleep(2000);
            }

            Engine.DeactivateNow();
        }
        catch (Exception ex)
        {
            LogService.Error($"--launch: {ex.Message}");
            try { Engine.DeactivateNow(); } catch { }
        }
    }
}
