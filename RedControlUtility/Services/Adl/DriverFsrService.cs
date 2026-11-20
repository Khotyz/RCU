using System.Runtime.InteropServices;
using System.Text;

namespace RedControlUtility.Services.Adl;

/// <summary>
/// FSR Upscaling upgrade (FSR 4 / ML) direto no driver, sem AMD Software.
/// O AMD Software apenas grava o valor de texto "FsrOverride" ("1" liga, "0" desliga) na chave UMD do driver
/// da placa, usando ADL2_Adapter_RegValueString_Get/Set do atiadlxx.dll (que vem com o driver). O driver lê o valor
/// quando o jogo abre, por isso o jogo precisa ser (re)iniciado depois de mudar. O suporte é consultado por
/// ADL2_DriverMLSRSupport_Get e pela presença do amdxcffx64.dll na pasta do driver.
/// Valor global por GPU: o motor de perfis liga ao abrir o jogo e restaura ao fechar, o que torna o efeito por jogo.
/// </summary>
public static unsafe partial class DriverFsrService
{
    private const int AdapterInfoSize = 1572;
    private const string SubKey = "UMD";
    private const string KeyName = "FsrOverride";
    private const int DriverPathOption = 1;

    private static readonly object Gate = new();
    private static readonly Dictionary<int, bool> SupportCache = new();

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
    private static IntPtr Alloc(int size) => Marshal.AllocHGlobal(Math.Max(1, size));

    internal sealed class Session : IDisposable
    {
        public IntPtr Module;
        public IntPtr Ctx;
        public int Adapter;
        public string Name = string.Empty;
        public IntPtr PGet, PSet, PSupport, PPath, PDestroy;

        public void Dispose()
        {
            try
            {
                if (Ctx != IntPtr.Zero && PDestroy != IntPtr.Zero)
                    ((delegate* unmanaged[Cdecl]<IntPtr, int>)PDestroy)(Ctx);
            }
            catch
            {
            }

            Ctx = IntPtr.Zero;
        }
    }

    internal static Session? Open(int gpuIndex)
    {
        string path = Path.Combine(Environment.SystemDirectory, "atiadlxx.dll");
        if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out IntPtr module)) return null;

        IntPtr Exp(string n) => NativeLibrary.TryGetExport(module, n, out IntPtr p) ? p : IntPtr.Zero;
        IntPtr pCreate = Exp("ADL2_Main_Control_Create"), pNum = Exp("ADL2_Adapter_NumberOfAdapters_Get"), pInfo = Exp("ADL2_Adapter_AdapterInfo_Get");
        var s = new Session
        {
            Module = module,
            PGet = Exp("ADL2_Adapter_RegValueString_Get"),
            PSet = Exp("ADL2_Adapter_RegValueString_Set"),
            PSupport = Exp("ADL2_DriverMLSRSupport_Get"),
            PPath = Exp("ADL2_Driver_Path_Get"),
            PDestroy = Exp("ADL2_Main_Control_Destroy")
        };
        if (pCreate == IntPtr.Zero || pNum == IntPtr.Zero || pInfo == IntPtr.Zero || s.PGet == IntPtr.Zero || s.PSet == IntPtr.Zero) return null;

        IntPtr ctx = IntPtr.Zero;
        var alloc = (IntPtr)(delegate* unmanaged[Stdcall]<int, IntPtr>)&Alloc;
        if (((delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr*, int>)pCreate)(alloc, 1, &ctx) != 0 || ctx == IntPtr.Zero) return null;
        s.Ctx = ctx;

        int n = 0;
        if (((delegate* unmanaged[Cdecl]<IntPtr, int*, int>)pNum)(ctx, &n) != 0 || n <= 0 || n > 64)
        {
            s.Dispose();
            return null;
        }

        int total = n * AdapterInfoSize;
        IntPtr buf = Marshal.AllocHGlobal(total + 512);
        try
        {
            new Span<byte>((void*)buf, total + 512).Clear();
            if (((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, int>)pInfo)(ctx, buf, total) != 0)
            {
                s.Dispose();
                return null;
            }

            var found = new List<(int Index, int Bus, string Name)>();
            for (int i = 0; i < n; i++)
            {
                byte* b = (byte*)buf + i * AdapterInfoSize;
                int index = *(int*)(b + 4);
                int bus = *(int*)(b + 264);
                int vendor = *(int*)(b + 276);
                string name = Marshal.PtrToStringAnsi((IntPtr)(b + 280), 256).Split('\0')[0];
                bool amd = vendor == 0x1002 || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || name.Contains("AMD", StringComparison.OrdinalIgnoreCase);
                if (amd && !found.Any(f => f.Bus == bus)) found.Add((index, bus, name));
            }

            if (found.Count == 0)
            {
                s.Dispose();
                return null;
            }

            var pick = found[Math.Clamp(gpuIndex, 0, found.Count - 1)];
            s.Adapter = pick.Index;
            s.Name = pick.Name;
            return s;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static bool DriverHasFfxDll(Session s)
    {
        if (s.PPath == IntPtr.Zero) return true; // não dá para checar: não bloqueia
        byte* buf = stackalloc byte[512];
        new Span<byte>(buf, 512).Clear();
        int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, byte*, int>)s.PPath)(s.Ctx, s.Adapter, 0x100, buf);
        if (rc != 0) return true;
        string dir = Marshal.PtrToStringAnsi((IntPtr)buf)?.Trim() ?? string.Empty;
        if (dir.Length == 0) return true;
        try
        {
            return File.Exists(Path.Combine(dir, "amdxcffx64.dll"));
        }
        catch
        {
            return true;
        }
    }

    /// <summary>O driver da GPU expõe o upgrade de FSR? (ADL2_DriverMLSRSupport_Get e amdxcffx64.dll).</summary>
    public static bool IsSupported(int gpuIndex = 0)
    {
        lock (Gate)
        {
            if (SupportCache.TryGetValue(gpuIndex, out bool cached)) return cached;
            bool ok = false;
            try
            {
                using var s = Open(gpuIndex);
                if (s != null)
                {
                    int supported = -1;
                    int rc = s.PSupport != IntPtr.Zero
                        ? ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)s.PSupport)(s.Ctx, s.Adapter, &supported)
                        : -1;
                    ok = rc == 0 ? supported != 0 : DriverHasFfxDll(s);
                    if (ok && !DriverHasFfxDll(s)) ok = false;
                    LogService.Info($"FSR driver: {s.Name} suporte={(rc == 0 ? supported.ToString() : "n/d")} => {(ok ? "sim" : "nao")}");
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"DriverFsrService.IsSupported: {ex.Message}");
            }

            SupportCache[gpuIndex] = ok;
            return ok;
        }
    }

    /// <summary>Estado atual do FsrOverride: true ligado, false desligado/ausente, null se não foi possível ler.</summary>
    public static bool? Get(int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return null;
            return ReadValue(s);
        }
        catch (Exception ex)
        {
            LogService.Error($"DriverFsrService.Get: {ex.Message}");
            return null;
        }
    }

    private static bool? ReadValue(Session s)
    {
        byte* val = stackalloc byte[0x400];
        new Span<byte>(val, 0x400).Clear();
        IntPtr sub = Marshal.StringToHGlobalAnsi(SubKey), key = Marshal.StringToHGlobalAnsi(KeyName);
        try
        {
            int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, IntPtr, int, byte*, int>)s.PGet)(
                s.Ctx, s.Adapter, DriverPathOption, sub, key, 0x400, val);
            if (rc != 0) return false; // sem valor gravado = desligado
            string text = val[1] == 0 && val[0] != 0
                ? Marshal.PtrToStringUni((IntPtr)val) ?? string.Empty
                : Marshal.PtrToStringAnsi((IntPtr)val) ?? string.Empty;
            return text.Trim() == "1";
        }
        finally
        {
            Marshal.FreeHGlobal(sub);
            Marshal.FreeHGlobal(key);
        }
    }

    /// <summary>Liga ou desliga o upgrade de FSR no driver. Devolve (ok, mensagem para o log).</summary>
    public static (bool Ok, string Message) Set(bool enable, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, UiHelper.L("Err.AtiMissing"));
            string value = enable ? "1" : "0";

            // Mesmo formato que o AMD Software usa: texto UTF-16, tamanho = caracteres * 2.
            byte[] data = Encoding.Unicode.GetBytes(value + "\0");
            IntPtr sub = Marshal.StringToHGlobalAnsi(SubKey), key = Marshal.StringToHGlobalAnsi(KeyName);
            IntPtr buf = Marshal.AllocHGlobal(data.Length + 16);
            try
            {
                new Span<byte>((void*)buf, data.Length + 16).Clear();
                Marshal.Copy(data, 0, buf, data.Length);
                int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, IntPtr, int, IntPtr, int>)s.PSet)(
                    s.Ctx, s.Adapter, DriverPathOption, sub, key, value.Length * 2, buf);
                if (rc != 0) return (false, $"ADL2_Adapter_RegValueString_Set => {rc}");
            }
            finally
            {
                Marshal.FreeHGlobal(sub);
                Marshal.FreeHGlobal(key);
                Marshal.FreeHGlobal(buf);
            }

            bool? back = ReadValue(s);
            return back == enable
                ? (true, $"FsrOverride={value} ({s.Name})")
                : (false, $"gravado, mas a leitura devolveu {(back == true ? "1" : "0")}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
