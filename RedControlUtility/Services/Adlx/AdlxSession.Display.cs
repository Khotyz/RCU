using RedControlUtility.Models;

namespace RedControlUtility.Services.Adlx;

public sealed partial class AdlxSession
{
    // IADLXDisplayServices (deriva de IADLXInterface)
    private static class DispSvc
    {
        public const int GetDisplays = 4, FreeSync = 9, Vsr = 10, GpuScaling = 11, ScalingMode = 12, IntegerScaling = 13, CustomColor = 16;
    }

    // IADLXDisplay
    private static class DispSlot
    {
        public const int Name = 6, NativeResolution = 8, RefreshRate = 9, UniqueId = 13;
    }

    // IADLXDisplayCustomColor: (supported, range, get, set) por propriedade
    private static readonly (int sup, int range, int get, int set)[] ColorSlots =
    {
        (3, 4, 5, 6),       // Hue
        (7, 8, 9, 10),      // Saturation
        (11, 12, 13, 14),   // Brightness
        (15, 16, 17, 18),   // Contrast
        (19, 20, 21, 22)    // Temperature
    };

    private List<AdlxObject> DisplayHandles()
    {
        var result = new List<AdlxObject>();
        if (_displays != null && _displays.GetPtr(DispSvc.GetDisplays, out IntPtr lp).Succeeded() && lp != IntPtr.Zero)
        {
            using var list = new AdlxObject(lp);
            foreach (var item in list.ListItems())
            {
                // Tipa o ponteiro como IADLXDisplay
                var typed = item.Query("IADLXDisplay");
                if (typed != null)
                {
                    item.Dispose();
                    result.Add(typed);
                }
                else
                {
                    result.Add(item);
                }
            }
        }

        return result;
    }

    private AdlxObject? DisplayFeature(AdlxObject display, int slot)
    {
        if (_displays == null) return null;
        return _displays.GetPtrWith(slot, display.Ptr, out IntPtr p).Succeeded() && p != IntPtr.Zero ? new AdlxObject(p) : null;
    }

    public List<DisplayInfo> ReadDisplays()
    {
        var result = new List<DisplayInfo>();
        lock (_sync)
        {
            var handles = DisplayHandles();
            try
            {
                int idx = 0;
                foreach (var d in handles)
                {
                    var info = new DisplayInfo { Index = idx++ };
                    if (d.GetString(DispSlot.Name, out string name).Succeeded()) info.Name = name;
                    if (d.GetInt2(DispSlot.NativeResolution, out int w, out int h).Succeeded())
                    {
                        info.NativeWidth = w;
                        info.NativeHeight = h;
                    }

                    if (d.GetDouble(DispSlot.RefreshRate, out double hz).Succeeded()) info.RefreshRate = hz;
                    if (d.GetPtr(DispSlot.UniqueId, out IntPtr uid).Succeeded()) info.UniqueId = (long)uid;

                    ReadDisplayFeatures(d, info);
                    result.Add(info);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"ReadDisplays: {ex.Message}");
            }
            finally
            {
                foreach (var h in handles) h.Dispose();
            }
        }

        return result;
    }

    private void ReadDisplayFeatures(AdlxObject d, DisplayInfo info)
    {
        var c = info.Caps;
        var s = info.Current;

        using (var f = DisplayFeature(d, DispSvc.FreeSync))
        {
            c.FreeSync = f != null && f.Supported();
            if (c.FreeSync && f!.GetBool(4, out bool e).Succeeded()) s.FreeSync = e;
        }

        // Escala da GPU, VSR, escala inteira e modo de escala: o driver costuma responder IsSupported=false enquanto a escala
        // da GPU esta desligada (o AMD Software tambem deixa essas opcoes cinzas). Se o driver entrega o objeto, a opcao fica
        // disponivel (mesmo bypass do AFMF): o resultado real aparece no relatorio de aplicacao.
        using (var f = DisplayFeature(d, DispSvc.GpuScaling))
        {
            c.GpuScaling = f != null;
            if (f != null)
            {
                if (!f.Supported()) LogService.Info($"Escala da GPU: IsSupported=false em '{info.Name}' (liberada mesmo assim)");
                if (f.GetBool(4, out bool e).Succeeded()) s.GpuScaling = e;
            }
        }

        using (var f = DisplayFeature(d, DispSvc.Vsr))
        {
            c.Vsr = f != null;
            if (f != null)
            {
                if (!f.Supported()) LogService.Info($"VSR: IsSupported=false em '{info.Name}' (liberado mesmo assim)");
                if (f.GetBool(4, out bool e).Succeeded()) s.Vsr = e;
            }
        }

        using (var f = DisplayFeature(d, DispSvc.IntegerScaling))
        {
            c.IntegerScaling = f != null;
            if (f != null && f.GetBool(4, out bool e).Succeeded()) s.IntegerScaling = e;
        }

        using (var f = DisplayFeature(d, DispSvc.ScalingMode))
        {
            c.ScalingMode = f != null;
            if (f != null && f.GetInt(4, out int m).Succeeded()) s.ScalingMode = m;
        }

        using (var f = DisplayFeature(d, DispSvc.CustomColor))
        {
            if (f != null)
            {
                for (int i = 0; i < ColorSlots.Length; i++)
                {
                    var sl = ColorSlots[i];
                    bool sup = f.Supported(sl.sup);
                    AdlxRange rg = AdlxRange.Empty;
                    int? val = null;
                    if (sup)
                    {
                        f.GetRange(sl.range, out rg);
                        if (f.GetInt(sl.get, out int v).Succeeded()) val = v;
                    }

                    switch (i)
                    {
                        case 0: c.Hue = sup; c.HueRange = rg; s.Hue = val; break;
                        case 1: c.Saturation = sup; c.SaturationRange = rg; s.Saturation = val; break;
                        case 2: c.Brightness = sup; c.BrightnessRange = rg; s.Brightness = val; break;
                        case 3: c.Contrast = sup; c.ContrastRange = rg; s.Contrast = val; break;
                        case 4: c.Temperature = sup; c.TemperatureRange = rg; s.Temperature = val; break;
                    }
                }
            }
        }
    }

    public ApplyReport ApplyDisplay(int displayIndex, DisplaySettings s)
    {
        var report = new ApplyReport();
        lock (_sync)
        {
            var handles = DisplayHandles();
            try
            {
                if (displayIndex < 0 || displayIndex >= handles.Count)
                {
                    report.Add("Display", AdlxResult.NotFound);
                    return report;
                }

                var d = handles[displayIndex];

                void Add(string name, bool? requested, AdlxResult result)
                {
                    if (requested != null) report.Add(name, result);
                }

                // force=true: o driver devolve IsSupported=false em vários monitores enquanto a escala da GPU
                // está desligada. Tentamos e mostramos o resultado real (o AMD Software também libera depois
                // que a escala é ligada).
                AdlxResult Toggle(string name, int svcSlot, bool? value, bool force = true)
                {
                    if (value == null) return AdlxResult.Ok;
                    using var f = DisplayFeature(d, svcSlot);
                    if (f == null) return AdlxResult.NotSupported;
                    if (!f.Supported() && !force) return AdlxResult.NotSupported;

                    var r = f.SetBool(5, value.Value);
                    if (!r.Succeeded()) return r;
                    if (f.GetBool(4, out bool now).Succeeded())
                    {
                        LogService.Info($"Display: {name} pedido {value.Value}, driver respondeu {now}");
                        if (now != value.Value)
                        {
                            // Uma segunda tentativa depois de o driver terminar de refazer o modo.
                            Thread.Sleep(150);
                            f.SetBool(5, value.Value);
                            if (f.GetBool(4, out bool again).Succeeded()) now = again;
                            if (now != value.Value) return AdlxResult.Fail;
                        }
                    }

                    return r;
                }

                bool ReadBool(int svcSlot, out bool on)
                {
                    on = false;
                    using var f = DisplayFeature(d, svcSlot);
                    return f != null && f.GetBool(4, out on).Succeeded();
                }

                // VSR e escala inteira só existem com a escala da GPU ligada: ela vai primeiro ao ligar e
                // por último ao desligar (mesma ordem do painel da AMD).
                bool scalingWillBeOn = s.GpuScaling ?? (ReadBool(DispSvc.GpuScaling, out bool gs) && gs);

                if (s.GpuScaling == false)
                {
                    Add("Virtual Super Resolution", s.Vsr, Toggle("Virtual Super Resolution", DispSvc.Vsr, s.Vsr));
                    Add(UiHelper.L("Report.IntegerScaling"), s.IntegerScaling, Toggle(UiHelper.L("Report.IntegerScaling"), DispSvc.IntegerScaling, s.IntegerScaling));
                    Add(UiHelper.L("Report.GpuScaling"), s.GpuScaling, Toggle(UiHelper.L("Report.GpuScaling"), DispSvc.GpuScaling, s.GpuScaling));
                }
                else
                {
                    Add(UiHelper.L("Report.GpuScaling"), s.GpuScaling, Toggle(UiHelper.L("Report.GpuScaling"), DispSvc.GpuScaling, s.GpuScaling));

                    // O VSR só funciona com a escala da GPU ligada: liga sozinho, como o AMD Software faz.
                    if (s.Vsr == true && !(ReadBool(DispSvc.GpuScaling, out bool gsNow) && gsNow))
                    {
                        LogService.Info("VSR pedido: ligando a escala da GPU (pré-requisito)");
                        report.Add(UiHelper.L("Report.GpuScalingForVsr"), Toggle(UiHelper.L("Report.GpuScaling"), DispSvc.GpuScaling, true));
                    }

                    // O driver refaz a lista de modos depois de mexer na escala: espera ele responder.
                    if (s.GpuScaling == true)
                    {
                        for (int i = 0; i < 10 && !(ReadBool(DispSvc.GpuScaling, out bool now) && now); i++) Thread.Sleep(100);
                    }

                    Add("Virtual Super Resolution", s.Vsr, Toggle("Virtual Super Resolution", DispSvc.Vsr, s.Vsr));
                    Add(UiHelper.L("Report.IntegerScaling"), s.IntegerScaling, Toggle(UiHelper.L("Report.IntegerScaling"), DispSvc.IntegerScaling, s.IntegerScaling));
                }

                if (s.ScalingMode != null)
                {
                    using var f = DisplayFeature(d, DispSvc.ScalingMode);
                    report.Add(UiHelper.L("Report.ScalingMode"), f == null ? AdlxResult.NotSupported : f.SetInt(5, s.ScalingMode.Value));
                }

                Add("FreeSync", s.FreeSync, Toggle("FreeSync", DispSvc.FreeSync, s.FreeSync, force: false));

                if (!scalingWillBeOn && s.Vsr == true)
                {
                    LogService.Info("VSR: a escala da GPU está desligada; o driver pode ignorar o VSR.");
                }

                int?[] colors = { s.Hue, s.Saturation, s.Brightness, s.Contrast, s.Temperature };
                string[] colorNames = { UiHelper.L("Report.Hue"), UiHelper.L("Report.Saturation"), UiHelper.L("Report.Brightness"), UiHelper.L("Report.Contrast"), UiHelper.L("Report.ColorTemp") };
                if (colors.Any(c => c != null))
                {
                    using var f = DisplayFeature(d, DispSvc.CustomColor);
                    for (int i = 0; i < colors.Length; i++)
                    {
                        if (colors[i] == null) continue;
                        if (f == null || !f.Supported(ColorSlots[i].sup))
                        {
                            report.Add(colorNames[i], AdlxResult.NotSupported);
                            continue;
                        }

                        f.GetRange(ColorSlots[i].range, out var rg);
                        int v = rg.IsValid ? rg.Clamp(colors[i]!.Value) : colors[i]!.Value;
                        report.Add(colorNames[i], f.SetInt(ColorSlots[i].set, v));
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"ApplyDisplay: {ex.Message}");
                report.Add("Display", AdlxResult.Fail);
            }
            finally
            {
                foreach (var h in handles) h.Dispose();
            }
        }

        foreach (var (feature, result) in report.Items)
        {
            LogService.Info($"Display[{displayIndex}]: {feature} => {result}");
        }

        return report;
    }
}
