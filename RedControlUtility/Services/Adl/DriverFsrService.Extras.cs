using System.Runtime.InteropServices;

namespace RedControlUtility.Services.Adl;

/// <summary>
/// Caminhos diretos pelo atiadlxx.dll, os mesmos que o AMD Software usa, para FRTC (ADL2_FPS_Settings_Set)
/// e escala da GPU (ADL2_DFP_GPUScalingEnable_Set). Servem de reforço ao ADLX, que em alguns drivers
/// devolve OK sem o driver realmente guardar o valor.
/// </summary>
public static unsafe partial class DriverFsrService
{
    private static IntPtr Export(Session s, string name) => NativeLibrary.TryGetExport(s.Module, name, out IntPtr p) ? p : IntPtr.Zero;

    /// <summary>
    /// FRTC pelo caminho antigo do ADL. Layout de <c>ADLFPSSettingsInput</c> (adl_structures.h):
    /// <c>int ulSize; int bGlobalSettings; int ulACFPSCurrent; int ulDCFPSCurrent; int ulReserved[6];</c> = 40 bytes.
    /// fps 0 desliga. Este caminho só é usado quando o driver não expõe FRTC pelo ADLX.
    /// </summary>
    public static (bool Ok, string Message) SetFrtc(bool enable, int fps, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, "atiadlxx.dll indisponivel");
            IntPtr pSet = Export(s, "ADL2_FPS_Settings_Set"), pGet = Export(s, "ADL2_FPS_Settings_Get"),
                   pCaps = Export(s, "ADL2_FPS_Caps");
            if (pSet == IntPtr.Zero) return (false, "ADL2_FPS_Settings_Set ausente");

            if (pCaps != IntPtr.Zero)
            {
                int supported = 0, version = 0;
                int crc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, int*, int>)pCaps)(s.Ctx, s.Adapter, &supported, &version);
                if (crc != 0 || supported == 0)
                {
                    return (false, $"o driver não expõe limite global de FPS (ADL2_FPS_Caps => {crc}, suporte={supported})");
                }
            }

            int value = enable ? Math.Max(1, fps) : 0;

            // Estrutura completa (40 bytes), com os 6 campos reservados zerados: passar um tamanho
            // menor faz o driver ler lixo e o limite sai errado (era o "cai para 5 fps").
            int* inp = stackalloc int[10];
            for (int i = 0; i < 10; i++) inp[i] = 0;
            inp[0] = 40;
            inp[1] = 1;      // bGlobalSettings
            inp[2] = value;  // AC
            inp[3] = value;  // DC
            int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)pSet)(s.Ctx, s.Adapter, inp);
            if (rc != 0) return (false, $"ADL2_FPS_Settings_Set => {rc}");

            string back = "";
            if (pGet != IntPtr.Zero)
            {
                // ADLFPSSettingsOutput: ulSize, bACFPSEnabled, bDCFPSEnabled, ulACFPSCurrent,
                // ulDCFPSCurrent, ulACFPSMaximum, ulACFPSMinimum, ulDCFPSMaximum, ulDCFPSMinimum = 36 bytes.
                int* o = stackalloc int[10];
                for (int i = 0; i < 10; i++) o[i] = 0;
                o[0] = 36;
                int g = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)pGet)(s.Ctx, s.Adapter, o);
                if (g == 0)
                {
                    // A ordem dos campos desta estrutura varia entre drivers; o registro bruto abaixo
                    // é o que permite (no log) conferir o que o driver guardou sem chutar índice.
                    back = $" | lido: [{string.Join(", ", Enumerable.Range(0, 9).Select(i => o[i]))}]";
                }
                else
                {
                    back = $" | leitura => {g}";
                }
            }

            return (true, $"FRTC {(enable ? fps + " fps" : "desligado")} ({s.Name}){back}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ADLDisplayInfo (adl_structures.h): displayID(16) + controllerIndex(4) + 2 x char[256] + 5 x int = 552 bytes.
    private const int DisplayInfoSize = 552;
    private const int DisplayInfoMaskOffset = 544, DisplayInfoValueOffset = 548;
    private const int ConnectedAndMapped = 0x3; // ADL_DISPLAY_DISPLAYINFO_DISPLAYCONNECTED | DISPLAYMAPPED

    private readonly record struct AdlDisplay(int Logical, int LogicalAdapter);

    /// <summary>Lista os monitores ligados e mapeados da placa (ADL2_Display_DisplayInfo_Get).</summary>
    private static List<AdlDisplay> ConnectedDisplays(Session s, out int rc)
    {
        var result = new List<AdlDisplay>();
        rc = -1;
        IntPtr pInfo = Export(s, "ADL2_Display_DisplayInfo_Get");
        if (pInfo == IntPtr.Zero) return result;

        int n = 0;
        IntPtr list = IntPtr.Zero;
        rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, IntPtr*, int, int>)pInfo)(s.Ctx, s.Adapter, &n, &list, 0);
        if (rc != 0 || list == IntPtr.Zero || n <= 0) return result;
        try
        {
            for (int i = 0; i < n; i++)
            {
                byte* b = (byte*)list + (long)i * DisplayInfoSize;
                int value = *(int*)(b + DisplayInfoValueOffset);
                if ((value & ConnectedAndMapped) != ConnectedAndMapped) continue;
                result.Add(new AdlDisplay(*(int*)b, *(int*)(b + 8)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(list);
        }

        return result;
    }

    /// <summary>
    /// Estado da escala da GPU pelo ADL: (alguma tela suporta, todas as telas suportadas estao ligadas, quantidade de telas, mensagem).
    /// Serve de reforco quando o ADLX diz "nao suportado" para a escala.
    /// </summary>
    public static (bool Supported, bool Enabled, int Count, string Message) GetGpuScaling(int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, false, 0, "atiadlxx.dll indisponivel");
            IntPtr pGet = Export(s, "ADL2_DFP_GPUScalingEnable_Get");
            if (pGet == IntPtr.Zero) return (false, false, 0, "ADL2_DFP_GPUScalingEnable_Get ausente");

            var displays = ConnectedDisplays(s, out int rc);
            if (displays.Count == 0) return (false, false, 0, $"nenhum monitor ligado/mapeado (rc={rc})");

            int supported = 0, enabled = 0;
            var log = new List<string>();
            foreach (var d in displays)
            {
                int sup = 0, cur = 0, def = 0;
                int g = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int*, int*, int>)pGet)(s.Ctx, d.LogicalAdapter, d.Logical, &sup, &cur, &def);
                log.Add($"tela {d.Logical}: rc={g} suporte={sup} atual={cur}");
                if (g == 0 && sup != 0)
                {
                    supported++;
                    if (cur != 0) enabled++;
                }
            }

            return (supported > 0, supported > 0 && enabled == supported, displays.Count, string.Join("; ", log));
        }
        catch (Exception ex)
        {
            return (false, false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Escala da GPU em todos os monitores ligados e mapeados da placa. Mesmo caminho do AMD Software:
    /// grava o valor persistente do driver (GPUScaling{adaptador}{tela}) e chama ADL2_DFP_GPUScalingEnable_Set.
    /// </summary>
    public static (bool Ok, string Message) SetGpuScaling(bool enable, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, "atiadlxx.dll indisponivel");
            IntPtr pSet = Export(s, "ADL2_DFP_GPUScalingEnable_Set"), pGet = Export(s, "ADL2_DFP_GPUScalingEnable_Get"),
                   pRegSet = Export(s, "ADL2_Adapter_RegValueInt_Set");
            if (pSet == IntPtr.Zero) return (false, "ADL2_DFP_GPUScalingEnable_Set ausente");

            var displays = ConnectedDisplays(s, out int rc);
            if (displays.Count == 0) return (false, $"nenhum monitor ligado/mapeado (ADL2_Display_DisplayInfo_Get => {rc})");

            var log = new List<string>();
            int okCount = 0;
            foreach (var d in displays)
            {
                if (pRegSet != IntPtr.Zero)
                {
                    IntPtr key = Marshal.StringToHGlobalAnsi($"GPUScaling{d.LogicalAdapter}{d.Logical}");
                    try
                    {
                        // ADL_REG_DEVICE_FUNCTION_1 = 1, sem subchave.
                        ((delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, IntPtr, int, int>)pRegSet)(s.Ctx, d.LogicalAdapter, 1, IntPtr.Zero, key, enable ? 1 : 0);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(key);
                    }
                }

                int r = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int>)pSet)(s.Ctx, d.LogicalAdapter, d.Logical, enable ? 1 : 0);
                string now = "";
                if (pGet != IntPtr.Zero)
                {
                    int sup = 0, cur = 0, def = 0;
                    int g = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int*, int*, int>)pGet)(s.Ctx, d.LogicalAdapter, d.Logical, &sup, &cur, &def);
                    now = g == 0 ? $" (suporte={sup}, atual={cur})" : $" (leitura {g})";
                    if (r == 0 && g == 0 && (cur != 0) != enable) r = -1; // o driver aceitou mas nao guardou
                }

                log.Add($"tela {d.Logical}: {(r == 0 ? "ok" : r.ToString())}{now}");
                if (r == 0) okCount++;
            }

            return (okCount > 0, string.Join("; ", log));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ------------------------------------------------------------------ Enhanced Sync

    private static string? ReadReg(Session s, string key)
    {
        byte* val = stackalloc byte[0x100];
        new Span<byte>(val, 0x100).Clear();
        IntPtr sub = Marshal.StringToHGlobalAnsi(SubKey), k = Marshal.StringToHGlobalAnsi(key);
        try
        {
            int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, IntPtr, int, byte*, int>)s.PGet)(
                s.Ctx, s.Adapter, DriverPathOption, sub, k, 0x100, val);
            if (rc != 0) return null;
            string text = val[1] == 0 && val[0] != 0
                ? Marshal.PtrToStringUni((IntPtr)val) ?? string.Empty
                : Marshal.PtrToStringAnsi((IntPtr)val) ?? string.Empty;
            return text.Trim();
        }
        finally
        {
            Marshal.FreeHGlobal(sub);
            Marshal.FreeHGlobal(k);
        }
    }

    /// <summary>
    /// Enhanced Sync pelo driver. Mesmo caminho do ADLX: suporte por ADL2_TurboSyncSupport_Get (e a chave UMD\TurboSync_NA),
    /// estado na chave de texto UMD\TurboSync ("1"/"0").
    /// </summary>
    public static (bool Supported, bool Enabled, string Message) GetEnhancedSync(int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, false, "atiadlxx.dll indisponivel");
            IntPtr pSup = Export(s, "ADL2_TurboSyncSupport_Get");
            int sup = 0;
            int rc = pSup != IntPtr.Zero ? ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)pSup)(s.Ctx, s.Adapter, &sup) : -1;
            string? na = ReadReg(s, "TurboSync_NA");
            string? cur = ReadReg(s, "TurboSync");
            bool supported = rc == 0 && sup == 1 && na != "1";
            return (supported, cur == "1", $"ADL2_TurboSyncSupport_Get rc={rc} valor={sup}, TurboSync_NA={na ?? "-"}, TurboSync={cur ?? "-"}");
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    public static (bool Ok, string Message) SetEnhancedSync(bool enable, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, "atiadlxx.dll indisponivel");

            // O ADLX grava o texto em ASCII com o tamanho de 1 caractere; o driver relê no inicio do jogo.
            IntPtr sub = Marshal.StringToHGlobalAnsi(SubKey), key = Marshal.StringToHGlobalAnsi("TurboSync");
            IntPtr buf = Marshal.AllocHGlobal(16);
            try
            {
                new Span<byte>((void*)buf, 16).Clear();
                *(byte*)buf = (byte)(enable ? '1' : '0');
                int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, IntPtr, int, IntPtr, int>)s.PSet)(
                    s.Ctx, s.Adapter, DriverPathOption, sub, key, 1, buf);
                if (rc != 0) return (false, $"ADL2_Adapter_RegValueString_Set => {rc}");
            }
            finally
            {
                Marshal.FreeHGlobal(sub);
                Marshal.FreeHGlobal(key);
                Marshal.FreeHGlobal(buf);
            }

            string notify = "";
            IntPtr pNotify = Export(s, "ADL2_User_Settings_Notify");
            if (pNotify != IntPtr.Zero)
            {
                int nrc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int>)pNotify)(s.Ctx, s.Adapter, 1, 1);
                notify = $", ADL2_User_Settings_Notify => {nrc}";
            }

            string? back = ReadReg(s, "TurboSync");
            bool ok = (back == "1") == enable;
            return (ok, $"TurboSync={(enable ? "1" : "0")} (leitura: {back ?? "-"}){notify}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ------------------------------------------------------------------ Monitores via ADL

    public sealed record AdlDisplayEntry(int Logical, string Name, bool ScalingSupported, bool ScalingOn);

    /// <summary>Monitores ligados e mapeados, com o estado da escala da GPU. Usado quando o ADLX nao lista monitores.</summary>
    public static List<AdlDisplayEntry> ListDisplays(int gpuIndex = 0)
    {
        var result = new List<AdlDisplayEntry>();
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return result;
            IntPtr pInfo = Export(s, "ADL2_Display_DisplayInfo_Get"), pGet = Export(s, "ADL2_DFP_GPUScalingEnable_Get");
            if (pInfo == IntPtr.Zero) return result;

            int n = 0;
            IntPtr list = IntPtr.Zero;
            int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, IntPtr*, int, int>)pInfo)(s.Ctx, s.Adapter, &n, &list, 0);
            if (rc != 0 || list == IntPtr.Zero || n <= 0) return result;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    byte* b = (byte*)list + (long)i * DisplayInfoSize;
                    if ((*(int*)(b + DisplayInfoValueOffset) & ConnectedAndMapped) != ConnectedAndMapped) continue;
                    int logical = *(int*)b, logicalAdapter = *(int*)(b + 8);
                    string name = Marshal.PtrToStringAnsi((IntPtr)(b + 20), 256)?.Split('\0')[0].Trim() ?? string.Empty;
                    int sup = 0, cur = 0, def = 0;
                    bool got = pGet != IntPtr.Zero &&
                               ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int*, int*, int>)pGet)(s.Ctx, logicalAdapter, logical, &sup, &cur, &def) == 0;
                    result.Add(new AdlDisplayEntry(logical, name, got && sup != 0, got && cur != 0));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(list);
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"DriverFsrService.ListDisplays: {ex.Message}");
        }

        return result;
    }

    // ------------------------------------------------------------------ Opcoes de texto do driver (UMD)

    /// <summary>Opcao liga/desliga guardada em UMD\\{key}; a chave {key}_NA = "1" marca como indisponivel.</summary>
    public static (bool Available, bool Enabled) GetUmdFlag(string key, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, false);
            bool na = ReadReg(s, key + "_NA") == "1";
            return (!na, ReadReg(s, key) == "1");
        }
        catch
        {
            return (false, false);
        }
    }

    public static (bool Ok, string Message) SetUmdFlag(string key, bool enable, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, "atiadlxx.dll indisponivel");
            IntPtr sub = Marshal.StringToHGlobalAnsi(SubKey), k = Marshal.StringToHGlobalAnsi(key);
            IntPtr buf = Marshal.AllocHGlobal(16);
            try
            {
                new Span<byte>((void*)buf, 16).Clear();
                *(byte*)buf = (byte)(enable ? '1' : '0');
                int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, IntPtr, int, IntPtr, int>)s.PSet)(
                    s.Ctx, s.Adapter, DriverPathOption, sub, k, 1, buf);
                if (rc != 0) return (false, $"ADL2_Adapter_RegValueString_Set => {rc}");
            }
            finally
            {
                Marshal.FreeHGlobal(sub);
                Marshal.FreeHGlobal(k);
                Marshal.FreeHGlobal(buf);
            }

            IntPtr pNotify = Export(s, "ADL2_User_Settings_Notify");
            if (pNotify != IntPtr.Zero) ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int>)pNotify)(s.Ctx, s.Adapter, 1, 1);

            string? back = ReadReg(s, key);
            return ((back == "1") == enable, $"{key}={(enable ? "1" : "0")} (leitura: {back ?? "-"})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
