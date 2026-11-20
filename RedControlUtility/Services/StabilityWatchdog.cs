using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace RedControlUtility.Services;

public sealed class StabilityWatchdog : IDisposable
{
    private const string Query =
        "*[System[(Provider[@Name='Display'] and EventID=4101) or " +
        "(Provider[@Name='amdkmdag' or @Name='amdwddmg' or @Name='amdkmpfd'] and (Level=1 or Level=2))]]";

    private const int EvtSubscribeToFutureEvents = 1;
    private const int EvtSubscribeActionDeliver = 1;
    private const int EvtRenderEventXml = 1;

    private delegate uint EvtSubscribeCallback(int action, IntPtr userContext, IntPtr hEvent);

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr EvtSubscribe(IntPtr session, IntPtr signalEvent, string channelPath, string query,
        IntPtr bookmark, IntPtr context, EvtSubscribeCallback callback, int flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    private static extern bool EvtClose(IntPtr handle);

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EvtRender(IntPtr context, IntPtr fragment, int flags, int bufferSize, IntPtr buffer, out int bufferUsed, out int propertyCount);

    private static readonly Regex ProviderRegex = new("<Provider Name='([^']+)'", RegexOptions.Compiled);
    private static readonly Regex IdRegex = new("<EventID[^>]*>(\\d+)</EventID>", RegexOptions.Compiled);

    private IntPtr _handle;
    private EvtSubscribeCallback? _callback;
    private DateTime _lastRaised = DateTime.MinValue;

    public event Action<string>? Unstable;

    public bool Running => _handle != IntPtr.Zero;

    public void Start()
    {
        if (_handle != IntPtr.Zero) return;
        try
        {
            _callback = OnEvent;
            _handle = EvtSubscribe(IntPtr.Zero, IntPtr.Zero, "System", Query, IntPtr.Zero, IntPtr.Zero, _callback, EvtSubscribeToFutureEvents);
            if (_handle == IntPtr.Zero)
            {
                LogService.Warn($"Vigia de estabilidade indisponível (erro {Marshal.GetLastWin32Error()}).");
                _callback = null;
                return;
            }

            LogService.Info("Vigia de estabilidade ativo (eventos de driver de vídeo).");
        }
        catch (Exception ex)
        {
            LogService.Warn($"Vigia de estabilidade indisponível: {ex.Message}");
            Stop();
        }
    }

    private uint OnEvent(int action, IntPtr userContext, IntPtr hEvent)
    {
        if (action != EvtSubscribeActionDeliver) return 0;
        try
        {
            var now = DateTime.UtcNow;
            if ((now - _lastRaised).TotalSeconds < 30) return 0;
            _lastRaised = now;
            Unstable?.Invoke(Describe(hEvent));
        }
        catch (Exception ex)
        {
            LogService.Error($"Vigia de estabilidade: {ex.Message}");
        }

        return 0;
    }

    private static string Describe(IntPtr hEvent)
    {
        IntPtr buf = Marshal.AllocHGlobal(16384);
        try
        {
            if (!EvtRender(IntPtr.Zero, hEvent, EvtRenderEventXml, 16384, buf, out _, out _)) return "evento do driver de video";
            string xml = Marshal.PtrToStringUni(buf) ?? string.Empty;
            string provider = ProviderRegex.Match(xml) is { Success: true } p ? p.Groups[1].Value : "driver";
            string id = IdRegex.Match(xml) is { Success: true } i ? i.Groups[1].Value : "?";
            return $"{provider} #{id}";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public void Stop()
    {
        var h = _handle;
        _handle = IntPtr.Zero;
        if (h != IntPtr.Zero)
        {
            try
            {
                EvtClose(h);
            }
            catch
            {
            }
        }

        _callback = null;
    }

    public void Dispose() => Stop();
}
