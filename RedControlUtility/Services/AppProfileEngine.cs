using System.Diagnostics;
using RedControlUtility.Models;
using RedControlUtility.Services.Adl;

namespace RedControlUtility.Services;

public sealed class AppProfileEngine : IDisposable
{
    private readonly HardwareService _hardware;
    private readonly Func<IEnumerable<AppProfile>> _profiles;
    private readonly Func<bool> _restoreOnExit;
    private Timer? _timer;
    private readonly object _lock = new();
    private bool _busy;

    private AppProfile? _active;
    private GraphicsSettings? _restoreGraphics;
    private TuningSettings? _restoreTuning;
    private bool? _restoreFsr;

    public AppProfile? Active => _active;

    public ApplyReport LastReport { get; private set; } = new();

    public event Action<AppProfile?>? ActiveChanged;

    public AppProfileEngine(HardwareService hardware, Func<IEnumerable<AppProfile>> profiles, Func<bool> restoreOnExit)
    {
        _hardware = hardware;
        _profiles = profiles;
        _restoreOnExit = restoreOnExit;
    }

    public bool IsRunning => _timer != null;

    public void Start()
    {
        Stop();
        _timer = new Timer(_ => Tick(), null, 500, 400);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        lock (_lock)
        {
            if (_active != null) Deactivate();
        }
    }

    private void Tick()
    {
        if (_busy) return;
        lock (_lock)
        {
            _busy = true;
            try
            {
                var running = RunningProcessNames(_profiles().Where(x => x.Enabled && !string.IsNullOrEmpty(x.ExeName)).Select(x => Path.GetFileNameWithoutExtension(x.ExeName)));
                var match = _profiles().FirstOrDefault(p => p.Enabled && !string.IsNullOrEmpty(p.ExeName) &&
                    running.Contains(Path.GetFileNameWithoutExtension(p.ExeName)));

                if (ReferenceEquals(match, _active)) return;
                if (match != null && _active != null && string.Equals(match.ExecutablePath, _active.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return;

                if (_active != null) Deactivate();
                if (match != null) Activate(match);
            }
            catch (Exception ex)
            {
                LogService.Error($"AppProfileEngine: {ex.Message}");
            }
            finally
            {
                _busy = false;
            }
        }
    }

    private static HashSet<string> RunningProcessNames(IEnumerable<string> wanted)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in wanted.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var list = Process.GetProcessesByName(name);
            if (list.Length > 0) set.Add(name);
            foreach (var p in list) p.Dispose();
        }

        return set;
    }

    private void Activate(AppProfile profile)
    {
        LogService.Info($"Perfil por app: ativando '{profile.Name}' ({profile.ExeName})");

        var gfx = profile.Graphics;

        _restoreFsr = null;
        if (gfx.Fsr != null)
        {
            bool fsrGpu = _hardware.CurrentGpu.Architecture.SupportsFsr4();
            if (fsrGpu && !_hardware.Caps.Graphics.Fsr && DriverFsrService.IsSupported())
            {
                _restoreFsr = DriverFsrService.Get();
                var fr = DriverFsrService.Set(gfx.Fsr.Value);
                LogService.Info($"Perfil '{profile.Name}' (FSR no driver): {(fr.Ok ? "ok" : "falhou")} - {fr.Message}");
                if (!fr.Ok) _restoreFsr = null;
            }

            if (!fsrGpu || !_hardware.Caps.Graphics.Fsr)
            {
                gfx = gfx.Clone();
                gfx.Fsr = null;
            }
        }

        var current = _hardware.ReadGraphics();
        _restoreGraphics = current.Mask(gfx);
        _restoreTuning = null;
        var report = new ApplyReport();

        if (!gfx.IsEmpty)
        {
            var r = _hardware.ApplyGraphics(gfx);
            LogService.Info($"Perfil '{profile.Name}' (gráficos): {r.Summary}");
            foreach (var item in r.Items) report.Add(item.Feature, item.Result);
        }
        else
        {
            LogService.Info($"Perfil '{profile.Name}': nenhuma opção de gráficos configurada.");
        }

        if ((profile.PowerLimitPercent != null || !string.IsNullOrEmpty(profile.TuningPreset)) && _hardware.Caps.Tuning.AnyTuning)
        {
            var cur = _hardware.ReadTuning();
            _restoreTuning = new TuningSettings
            {
                PowerLimitPercent = profile.PowerLimitPercent != null ? cur.PowerLimitPercent : null,
                Preset = !string.IsNullOrEmpty(profile.TuningPreset) ? cur.Preset : null
            };
            var t = new TuningSettings { PowerLimitPercent = profile.PowerLimitPercent, Preset = profile.TuningPreset };
            var rt = _hardware.ApplyTuning(t);
            LogService.Info($"Perfil '{profile.Name}' (tuning): {rt.Summary}");
            foreach (var item in rt.Items) report.Add(item.Feature, item.Result);
        }

        LastReport = report;
        _active = profile;
        ActiveChanged?.Invoke(profile);
    }

    private void Deactivate()
    {
        var profile = _active;
        LogService.Info($"Perfil por app: encerrando '{profile?.Name}'");

        if (_restoreOnExit() && profile?.KeepAfterExit != true)
        {
            if (_restoreGraphics != null && !_restoreGraphics.IsEmpty)
            {
                var r = _hardware.ApplyGraphics(_restoreGraphics);
                LogService.Info($"Restaurar gráficos: {r.Summary}");
            }

            if (_restoreTuning != null)
            {
                var r = _hardware.ApplyTuning(_restoreTuning);
                LogService.Info($"Restaurar tuning: {r.Summary}");
            }

            if (_restoreFsr != null)
            {
                var r = DriverFsrService.Set(_restoreFsr.Value);
                LogService.Info($"Restaurar FSR no driver: {(r.Ok ? "ok" : "falhou")} - {r.Message}");
            }
        }

        _active = null;
        _restoreGraphics = null;
        _restoreTuning = null;
        _restoreFsr = null;
        ActiveChanged?.Invoke(null);
    }

    public ApplyReport ApplyNow(AppProfile profile)
    {
        lock (_lock)
        {
            try
            {
                if (_active != null) Deactivate();
                Activate(profile);
            }
            catch (Exception ex)
            {
                LogService.Error($"Aplicar perfil agora: {ex.Message}");
            }

            return LastReport;
        }
    }

    public void ActivateNow(AppProfile profile)
    {
        lock (_lock)
        {
            if (_active != null) Deactivate();
            Activate(profile);
        }
    }

    public void DeactivateNow()
    {
        lock (_lock)
        {
            if (_active != null) Deactivate();
        }
    }

    public void Dispose() => Stop();
}
