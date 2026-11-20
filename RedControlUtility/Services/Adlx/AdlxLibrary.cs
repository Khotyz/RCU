using System.Runtime.InteropServices;
using System.Text;

namespace RedControlUtility.Services.Adlx;

public enum AdlxBackendKind
{
    Auto = 0,
    AmdDll = 1,
    HelperDll = 2
}

public sealed unsafe class AdlxLibrary : IDisposable
{
    private const ulong SdkFullVersion = (2UL << 48) | (0UL << 32) | (0UL << 16) | 125UL;

    private IntPtr _module;
    private bool _isHelper;
    private delegate* unmanaged[Cdecl]<int> _terminate;
    private delegate* unmanaged[Cdecl]<int> _helperTerminate;

    public string BackendName { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public ulong FullVersion { get; private set; }

    public IntPtr System { get; private set; }
    public IntPtr AdlMapping { get; private set; }

    private AdlxLibrary()
    {
    }

    public static AdlxLibrary? Open(AdlxBackendKind kind, out string report)
    {
        var log = new StringBuilder();
        AdlxLibrary? lib = null;

        if (kind is AdlxBackendKind.Auto or AdlxBackendKind.AmdDll)
        {
            lib = TryOpenAmd(log);
        }

        if (lib == null && kind is AdlxBackendKind.Auto or AdlxBackendKind.HelperDll)
        {
            lib = TryOpenHelper(log);
        }

        report = log.ToString().Trim();
        return lib;
    }

    public static AdlxLibrary? OpenAttempt(string which, StringBuilder log) => which switch
    {
        "amd" => TryOpenAmd(log, false),
        "amd-driver" => TryOpenAmd(log, true),
        _ => TryOpenHelper(log)
    };

    private static AdlxLibrary? TryOpenAmd(StringBuilder log, bool driverVersionOnly = false)
    {
        string path = Path.Combine(Environment.SystemDirectory, "amdadlx64.dll");
        if (!File.Exists(path))
        {
            log.AppendLine(UiHelper.L("Diag.AmdMissing", path));
            return null;
        }

        if (!NativeLibrary.TryLoad(path, out IntPtr module))
        {
            log.AppendLine(UiHelper.L("Diag.AmdLoadFail", path));
            return null;
        }

        var lib = new AdlxLibrary { _module = module, BackendName = "AMD amdadlx64.dll (driver)" };

        try
        {
            _ = NativeLibrary.TryGetExport(module, "ADLXQueryFullVersion", out IntPtr pFull);
            _ = NativeLibrary.TryGetExport(module, "ADLXQueryVersion", out IntPtr pVer);
            _ = NativeLibrary.TryGetExport(module, "ADLXInitialize2", out IntPtr pInit2);
            _ = NativeLibrary.TryGetExport(module, "ADLXInitializeWithIncompatibleDriver2", out IntPtr pInit2Inc);
            _ = NativeLibrary.TryGetExport(module, "ADLXInitialize", out IntPtr pInit);
            _ = NativeLibrary.TryGetExport(module, "ADLXInitializeWithIncompatibleDriver", out IntPtr pInitInc);
            _ = NativeLibrary.TryGetExport(module, "ADLXTerminate", out IntPtr pTerm);

            if (pTerm == IntPtr.Zero || (pInit2 == IntPtr.Zero && pInit == IntPtr.Zero))
            {
                log.AppendLine(UiHelper.L("Diag.AmdNoExports"));
                lib.Dispose();
                return null;
            }

            lib._terminate = (delegate* unmanaged[Cdecl]<int>)pTerm;

            ulong driverVersion = 0;
            if (pFull != IntPtr.Zero)
            {
                ulong v = 0;
                ((delegate* unmanaged[Cdecl]<ulong*, int>)pFull)(&v);
                driverVersion = v;
            }
            lib.FullVersion = driverVersion;

            if (pVer != IntPtr.Zero)
            {
                IntPtr s = IntPtr.Zero;
                ((delegate* unmanaged[Cdecl]<IntPtr*, int>)pVer)(&s);
                if (s != IntPtr.Zero)
                {
                    lib.Version = Marshal.PtrToStringAnsi(s) ?? string.Empty;
                }
            }

            log.AppendLine($"[AMD] ADLX do driver: {FormatVersion(driverVersion)} (\"{lib.Version}\"); SDK do app: {FormatVersion(SdkFullVersion)}.");

            var versionsToTry = new List<ulong>();
            if (driverVersion > 0) versionsToTry.Add(driverVersion);
            if (!driverVersionOnly && !versionsToTry.Contains(SdkFullVersion)) versionsToTry.Add(SdkFullVersion);
            if (versionsToTry.Count == 0) versionsToTry.Add(SdkFullVersion);

            AdlxResult last = AdlxResult.Fail;
            foreach (ulong version in versionsToTry)
            {
                foreach (bool incompatible in new[] { false, true })
                {
                    IntPtr system = IntPtr.Zero;
                    IntPtr mapping = IntPtr.Zero;
                    IntPtr fn2 = incompatible ? pInit2Inc : pInit2;
                    IntPtr fn1 = incompatible ? pInitInc : pInit;

                    if (fn2 != IntPtr.Zero)
                    {
                        last = (AdlxResult)((delegate* unmanaged[Cdecl]<ulong, IntPtr*, IntPtr*, int>)fn2)(version, &system, &mapping);
                    }
                    else if (fn1 != IntPtr.Zero)
                    {
                        last = (AdlxResult)((delegate* unmanaged[Cdecl]<ulong, IntPtr*, int>)fn1)(version, &system);
                    }
                    else
                    {
                        continue;
                    }

                    log.AppendLine(UiHelper.L("Diag.AmdInit", FormatVersion(version), incompatible, last, last.Describe()));
                    if (last.Succeeded() && system != IntPtr.Zero)
                    {
                        lib.System = system;
                        lib.AdlMapping = mapping;
                        if (incompatible)
                        {
                            lib.BackendName += " [modo driver legado]";
                        }
                        return lib;
                    }
                }
            }

            log.AppendLine(UiHelper.L("Diag.AmdInitFail", last));
        }
        catch (Exception ex)
        {
            log.AppendLine(UiHelper.L("Diag.AmdException", ex.Message));
        }

        lib.Dispose();
        return null;
    }

    private static AdlxLibrary? TryOpenHelper(StringBuilder log)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "RedAdlxHelper.dll");
        if (!File.Exists(path))
        {
            log.AppendLine(UiHelper.L("Diag.HelperMissing"));
            return null;
        }

        if (!NativeLibrary.TryLoad(path, out IntPtr module))
        {
            log.AppendLine("[Helper] Falha ao carregar RedAdlxHelper.dll (se for build Debug, precisa do VC++ Debug runtime: VCRUNTIME140D/ucrtbased; compile em Release).");
            return null;
        }

        var lib = new AdlxLibrary { _module = module, _isHelper = true, BackendName = "RedAdlxHelper.dll (helper)" };
        try
        {
            _ = NativeLibrary.TryGetExport(module, "ADLXHelper_Initialize", out IntPtr pInit);
            _ = NativeLibrary.TryGetExport(module, "ADLXHelper_GetSystemServices", out IntPtr pSys);
            _ = NativeLibrary.TryGetExport(module, "ADLXHelper_Terminate", out IntPtr pTerm);
            if (pInit == IntPtr.Zero || pSys == IntPtr.Zero)
            {
                log.AppendLine("[Helper] A DLL helper não exporta ADLXHelper_Initialize/ADLXHelper_GetSystemServices.");
                lib.Dispose();
                return null;
            }

            if (pTerm != IntPtr.Zero)
            {
                lib._helperTerminate = (delegate* unmanaged[Cdecl]<int>)pTerm;
            }

            var r = (AdlxResult)((delegate* unmanaged[Cdecl]<int>)pInit)();
            log.AppendLine($"[Helper] ADLXHelper_Initialize => {r} ({r.Describe()})");
            if (!r.Succeeded())
            {
                lib.Dispose();
                return null;
            }

            IntPtr system = ((delegate* unmanaged[Cdecl]<IntPtr>)pSys)();
            if (system == IntPtr.Zero)
            {
                log.AppendLine("[Helper] ADLXHelper_GetSystemServices retornou NULL.");
                lib.Dispose();
                return null;
            }

            lib.System = system;
            return lib;
        }
        catch (Exception ex)
        {
            log.AppendLine($"[Helper] Exceção: {ex.Message}");
        }

        lib.Dispose();
        return null;
    }

    public void Dispose()
    {
        try
        {
            if (System != IntPtr.Zero)
            {
                if (_isHelper)
                {
                    if (_helperTerminate != null) _helperTerminate();
                }
                else if (_terminate != null)
                {
                    _terminate();
                }
            }
        }
        catch
        {
        }

        System = IntPtr.Zero;
        AdlMapping = IntPtr.Zero;
        if (_module != IntPtr.Zero)
        {
            try { NativeLibrary.Free(_module); } catch { }
            _module = IntPtr.Zero;
        }
    }

    public static string FormatVersion(ulong v) =>
        $"{(v >> 48) & 0xFFFF}.{(v >> 32) & 0xFFFF}.{(v >> 16) & 0xFFFF}.{v & 0xFFFF}";
}
