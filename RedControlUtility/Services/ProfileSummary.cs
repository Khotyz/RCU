using RedControlUtility.Models;
using static RedControlUtility.UiHelper;

namespace RedControlUtility.Services;

/// <summary>Resumo curto e minimalista das opções que um perfil de app aplica (mostrado no overlay ao abrir o jogo).</summary>
public static class ProfileSummary
{
    public static List<OverlayItem> Build(AppProfile p, GraphicsCapabilities c, bool fsrAvailable)
    {
        var g = p.Graphics;
        var items = new List<OverlayItem>();
        string OnOff(bool v) => v ? L("Common.On") : L("Common.Off");
        void Flag(string name, bool? v) { if (v != null) items.Add(new OverlayItem(name, OnOff(v.Value), v.Value)); }
        void Val(string name, bool? on, string value)
        {
            if (on == null) return;
            items.Add(new OverlayItem(name, on.Value ? value : L("Common.Off"), on.Value));
        }

        if (c.AntiLag) Flag("Anti-Lag", g.AntiLag);
        if (c.Afmf) Flag("AFMF", g.Afmf);
        if (fsrAvailable) Flag("FSR 4", g.Fsr);
        if (c.Rsr) Val("RSR", g.Rsr, g.RsrSharpness != null ? $"{g.RsrSharpness}%" : L("Common.On"));
        if (c.ImageSharpening) Val("Image Sharpening", g.ImageSharpening, g.ImageSharpeningLevel != null ? $"{g.ImageSharpeningLevel}%" : L("Common.On"));
        if (c.Boost) Flag("Radeon Boost", g.Boost);
        if (c.EnhancedSync) Flag("Enhanced Sync", g.EnhancedSync);
        if (c.VSync && g.VSyncMode != null) items.Add(new OverlayItem("V-Sync", g.VSyncMode.ToString() ?? "", true));
        if (c.Chill) Val("Radeon Chill", g.Chill, g.ChillMinFps != null && g.ChillMaxFps != null ? $"{g.ChillMinFps}-{g.ChillMaxFps} fps" : L("Common.On"));
        if (c.Frtc) Val("FRTC", g.Frtc, g.FrtcFps != null ? $"{g.FrtcFps} fps" : L("Common.On"));
        if (p.PowerLimitPercent != null) items.Add(new OverlayItem(L("Notify.Power"), $"{p.PowerLimitPercent:+0;-0;0}%", true));
        if (!string.IsNullOrEmpty(p.TuningPreset)) items.Add(new OverlayItem("Preset", p.TuningPreset, true));
        return items;
    }
}
