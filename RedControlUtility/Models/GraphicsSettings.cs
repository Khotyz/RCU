using RedControlUtility.Services.Adlx;

namespace RedControlUtility.Models;

public sealed class GraphicsSettings
{
    public bool? AntiLag { get; set; }
    public bool? Afmf { get; set; }
    public bool? Fsr { get; set; }
    public bool? Chill { get; set; }
    public int? ChillMinFps { get; set; }
    public int? ChillMaxFps { get; set; }
    public bool? Frtc { get; set; }
    public int? FrtcFps { get; set; }
    public bool? ImageSharpening { get; set; }
    public int? ImageSharpeningLevel { get; set; }
    public bool? Rsr { get; set; }
    public int? RsrSharpness { get; set; }
    public bool? EnhancedSync { get; set; }
    public bool? Boost { get; set; }
    public int? BoostResolution { get; set; }
    public int? VSyncMode { get; set; }
    public bool? AnisotropicFiltering { get; set; }
    public int? AnisotropicLevel { get; set; }
    public int? TessellationMode { get; set; }
    public int? TessellationLevel { get; set; }
    public int? AaMode { get; set; }
    public int? AaMethod { get; set; }
    public int? AaLevel { get; set; }
    public bool? Mlaa { get; set; }
    public bool? TripleBuffer { get; set; }
    public bool? SurfaceFormatOpt { get; set; }

    public GraphicsSettings Clone() => (GraphicsSettings)MemberwiseClone();

    public bool IsEmpty =>
        AntiLag == null && Afmf == null && Fsr == null && Chill == null && ChillMinFps == null && ChillMaxFps == null &&
        Frtc == null && FrtcFps == null && ImageSharpening == null && ImageSharpeningLevel == null && Rsr == null &&
        RsrSharpness == null && EnhancedSync == null && Boost == null && BoostResolution == null && VSyncMode == null &&
        AnisotropicFiltering == null && AnisotropicLevel == null && TessellationMode == null && TessellationLevel == null &&
        AaMode == null && AaMethod == null && AaLevel == null && Mlaa == null && TripleBuffer == null && SurfaceFormatOpt == null;

    public GraphicsSettings Mask(GraphicsSettings mask)
    {
        var r = new GraphicsSettings();
        if (mask.AntiLag != null) r.AntiLag = AntiLag;
        if (mask.Afmf != null) r.Afmf = Afmf;
        if (mask.Fsr != null) r.Fsr = Fsr;
        if (mask.EnhancedSync != null) r.EnhancedSync = EnhancedSync;
        if (mask.VSyncMode != null) r.VSyncMode = VSyncMode;
        if (mask.Chill != null || mask.ChillMinFps != null || mask.ChillMaxFps != null)
        {
            r.Chill = Chill; r.ChillMinFps = ChillMinFps; r.ChillMaxFps = ChillMaxFps;
        }
        if (mask.Frtc != null || mask.FrtcFps != null)
        {
            r.Frtc = Frtc; r.FrtcFps = FrtcFps;
        }
        if (mask.ImageSharpening != null || mask.ImageSharpeningLevel != null)
        {
            r.ImageSharpening = ImageSharpening; r.ImageSharpeningLevel = ImageSharpeningLevel;
        }
        if (mask.Rsr != null || mask.RsrSharpness != null)
        {
            r.Rsr = Rsr; r.RsrSharpness = RsrSharpness;
        }
        if (mask.Boost != null || mask.BoostResolution != null)
        {
            r.Boost = Boost; r.BoostResolution = BoostResolution;
        }
        if (mask.AnisotropicFiltering != null || mask.AnisotropicLevel != null)
        {
            r.AnisotropicFiltering = AnisotropicFiltering; r.AnisotropicLevel = AnisotropicLevel;
        }
        if (mask.TessellationMode != null || mask.TessellationLevel != null)
        {
            r.TessellationMode = TessellationMode; r.TessellationLevel = TessellationLevel;
        }
        if (mask.AaMode != null || mask.AaMethod != null || mask.AaLevel != null)
        {
            r.AaMode = AaMode; r.AaMethod = AaMethod; r.AaLevel = AaLevel;
        }
        if (mask.Mlaa != null) r.Mlaa = Mlaa;
        if (mask.TripleBuffer != null) r.TripleBuffer = TripleBuffer;
        if (mask.SurfaceFormatOpt != null) r.SurfaceFormatOpt = SurfaceFormatOpt;
        return r;
    }

    public void Overlay(GraphicsSettings o)
    {
        AntiLag = o.AntiLag ?? AntiLag;
        Afmf = o.Afmf ?? Afmf;
        Fsr = o.Fsr ?? Fsr;
        Chill = o.Chill ?? Chill;
        ChillMinFps = o.ChillMinFps ?? ChillMinFps;
        ChillMaxFps = o.ChillMaxFps ?? ChillMaxFps;
        Frtc = o.Frtc ?? Frtc;
        FrtcFps = o.FrtcFps ?? FrtcFps;
        ImageSharpening = o.ImageSharpening ?? ImageSharpening;
        ImageSharpeningLevel = o.ImageSharpeningLevel ?? ImageSharpeningLevel;
        Rsr = o.Rsr ?? Rsr;
        RsrSharpness = o.RsrSharpness ?? RsrSharpness;
        EnhancedSync = o.EnhancedSync ?? EnhancedSync;
        Boost = o.Boost ?? Boost;
        BoostResolution = o.BoostResolution ?? BoostResolution;
        VSyncMode = o.VSyncMode ?? VSyncMode;
        AnisotropicFiltering = o.AnisotropicFiltering ?? AnisotropicFiltering;
        AnisotropicLevel = o.AnisotropicLevel ?? AnisotropicLevel;
        TessellationMode = o.TessellationMode ?? TessellationMode;
        TessellationLevel = o.TessellationLevel ?? TessellationLevel;
        AaMode = o.AaMode ?? AaMode;
        AaMethod = o.AaMethod ?? AaMethod;
        AaLevel = o.AaLevel ?? AaLevel;
        Mlaa = o.Mlaa ?? Mlaa;
        TripleBuffer = o.TripleBuffer ?? TripleBuffer;
        SurfaceFormatOpt = o.SurfaceFormatOpt ?? SurfaceFormatOpt;
    }
}

public sealed class GraphicsCapabilities
{
    public bool AntiLag { get; set; }
    public bool Afmf { get; set; }
    public bool Fsr { get; set; }
    public bool Chill { get; set; }
    public AdlxRange ChillRange { get; set; } = AdlxRange.Empty;
    public bool Frtc { get; set; }
    public AdlxRange FrtcRange { get; set; } = AdlxRange.Empty;
    public bool ImageSharpening { get; set; }
    public AdlxRange ImageSharpeningRange { get; set; } = AdlxRange.Empty;
    public bool Rsr { get; set; }
    public AdlxRange RsrRange { get; set; } = AdlxRange.Empty;
    public bool EnhancedSync { get; set; }
    public bool Boost { get; set; }
    public AdlxRange BoostRange { get; set; } = AdlxRange.Empty;
    public bool VSync { get; set; }
    public bool AnisotropicFiltering { get; set; }
    public bool Tessellation { get; set; }
    public bool ResetShaderCache { get; set; }
    public bool AntiAliasing { get; set; }
    public bool Mlaa { get; set; }
    public bool TripleBuffer { get; set; }
    public bool SurfaceFormatOpt { get; set; }

    public bool Any => AntiLag || Afmf || Fsr || Chill || Frtc || ImageSharpening || Rsr || EnhancedSync || Boost || VSync ||
                       AnisotropicFiltering || Tessellation || AntiAliasing || Mlaa || TripleBuffer || SurfaceFormatOpt;
}
