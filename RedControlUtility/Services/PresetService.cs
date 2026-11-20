using RedControlUtility.Models;

namespace RedControlUtility.Services;

/// <summary>Presets rápidos de um clique. Todos são derivados das faixas reais informadas pelo driver.</summary>
public static class PresetService
{
    public static readonly string[] Keys = { "SafeUndervolt", "Quiet", "Balanced", "Performance" };

    /// <summary>Retorna null para "Balanced" (restaurar padrão de fábrica) ou se a GPU não suporta ajuste.</summary>
    public static TuningSettings? Build(string key, TuningCapabilities caps, TuningSettings current, TuningSettings? baseline)
    {
        if (!caps.AnyTuning || key == "Balanced") return null;

        int voltDelta = key switch { "SafeUndervolt" => -25, "Quiet" => -40, _ => 0 };
        int? power = key switch
        {
            "Quiet" => -10,
            "Performance" => caps.PowerRange.IsValid ? caps.PowerRange.Max : null,
            _ => 0
        };

        var s = new TuningSettings();
        if (caps.GfxMode == GfxTuningMode.MinMaxVoltage && caps.VoltageRange.IsValid)
        {
            int basev = caps.DefaultVoltage ?? baseline?.VoltageMv ?? current.VoltageMv ?? caps.VoltageRange.Max;
            s.VoltageMv = caps.VoltageRange.Clamp(basev + voltDelta);
        }
        else if (caps.GfxMode == GfxTuningMode.StateList && (baseline?.VfStates ?? current.VfStates) is { Count: > 0 } src)
        {
            s.VfStates = src.Select(p =>
            {
                var c = p.Clone();
                int v = p.VoltageMv + voltDelta;
                c.VoltageMv = caps.StateVoltageRange.IsValid ? caps.StateVoltageRange.Clamp(v) : v;
                return c;
            }).ToList();
        }

        if (caps.PowerSupported && power != null)
        {
            s.PowerLimitPercent = caps.PowerRange.IsValid ? caps.PowerRange.Clamp(power.Value) : power;
        }

        return s;
    }
}
