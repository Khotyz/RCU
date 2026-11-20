using System.Globalization;
using System.Text.RegularExpressions;
using RedControlUtility.Models;

namespace RedControlUtility.Services;

public static class GpuArchitectureDetector
{
    private readonly record struct Entry(int Low, int High, GpuArchitecture Arch, string Chip);

    private static readonly Entry[] Table =
    {
        new(0x67A0, 0x67BF, GpuArchitecture.Gcn, "Hawaii"),
        new(0x6920, 0x693F, GpuArchitecture.Gcn, "Tonga"),
        new(0x7300, 0x730F, GpuArchitecture.Gcn, "Fiji"),
        new(0x67C0, 0x67DF, GpuArchitecture.Polaris, "Polaris 10"),
        new(0x67E0, 0x67FF, GpuArchitecture.Polaris, "Polaris 11"),
        new(0x6980, 0x699F, GpuArchitecture.Polaris, "Polaris 12"),
        new(0x6FDF, 0x6FDF, GpuArchitecture.Polaris, "Polaris 20"),
        new(0x6860, 0x687F, GpuArchitecture.Vega, "Vega 10"),
        new(0x66A0, 0x66AF, GpuArchitecture.Vega, "Vega 20"),
        new(0x15D8, 0x15D8, GpuArchitecture.Vega, "Picasso (iGPU)"),
        new(0x15DD, 0x15DD, GpuArchitecture.Vega, "Raven (iGPU)"),
        new(0x15E7, 0x15E7, GpuArchitecture.Vega, "Barcelo (iGPU)"),
        new(0x1636, 0x1636, GpuArchitecture.Vega, "Renoir (iGPU)"),
        new(0x1638, 0x1638, GpuArchitecture.Vega, "Cezanne (iGPU)"),
        new(0x164C, 0x164C, GpuArchitecture.Vega, "Lucienne (iGPU)"),
        new(0x7310, 0x731F, GpuArchitecture.Rdna1, "Navi 10"),
        new(0x7340, 0x734F, GpuArchitecture.Rdna1, "Navi 14"),
        new(0x7360, 0x736F, GpuArchitecture.Rdna1, "Navi 12"),
        new(0x73A0, 0x73BF, GpuArchitecture.Rdna2, "Navi 21"),
        new(0x73C0, 0x73DF, GpuArchitecture.Rdna2, "Navi 22"),
        new(0x73E0, 0x73FF, GpuArchitecture.Rdna2, "Navi 23"),
        new(0x7420, 0x743F, GpuArchitecture.Rdna2, "Navi 24"),
        new(0x163F, 0x163F, GpuArchitecture.Rdna2, "Van Gogh (iGPU)"),
        new(0x164D, 0x164D, GpuArchitecture.Rdna2, "Yellow Carp (iGPU)"),
        new(0x164E, 0x164E, GpuArchitecture.Rdna2, "Raphael (iGPU)"),
        new(0x1681, 0x1681, GpuArchitecture.Rdna2, "Rembrandt (iGPU)"),
        new(0x7440, 0x744F, GpuArchitecture.Rdna3, "Navi 31"),
        new(0x7460, 0x747F, GpuArchitecture.Rdna3, "Navi 32"),
        new(0x7480, 0x748F, GpuArchitecture.Rdna3, "Navi 33"),
        new(0x15BF, 0x15BF, GpuArchitecture.Rdna3, "Phoenix (iGPU)"),
        new(0x15C8, 0x15C8, GpuArchitecture.Rdna3, "Phoenix 2 (iGPU)"),
        new(0x1900, 0x1901, GpuArchitecture.Rdna3, "Hawk Point (iGPU)"),
        new(0x150E, 0x150E, GpuArchitecture.Rdna3_5, "Strix Point (iGPU)"),
        new(0x1586, 0x1586, GpuArchitecture.Rdna3_5, "Strix Halo (iGPU)"),
        new(0x1114, 0x1114, GpuArchitecture.Rdna3_5, "Krackan (iGPU)"),
        new(0x7550, 0x755F, GpuArchitecture.Rdna4, "Navi 48"),
        new(0x7590, 0x759F, GpuArchitecture.Rdna4, "Navi 44"),
    };

    private static readonly (Regex Pattern, GpuArchitecture Arch)[] NameRules =
    {
        (new Regex(@"\bRX\s*9\d{3}|\bAI\s*PRO\s*R9\d{3}|\bR9700", RegexOptions.IgnoreCase), GpuArchitecture.Rdna4),
        (new Regex(@"\b(890M|880M|8060S|8050S|8040S)\b", RegexOptions.IgnoreCase), GpuArchitecture.Rdna3_5),
        (new Regex(@"\bRX\s*7\d{3}|\b7[0-9]{2}M\b|\bW7[0-9]{3}", RegexOptions.IgnoreCase), GpuArchitecture.Rdna3),
        (new Regex(@"\bRX\s*6\d{3}|\b6[0-9]{2}M\b|\bW6[0-9]{3}", RegexOptions.IgnoreCase), GpuArchitecture.Rdna2),
        (new Regex(@"\bRX\s*5[3-7]00|\bW5[0-9]00", RegexOptions.IgnoreCase), GpuArchitecture.Rdna1),
        (new Regex(@"\bVega\b|Radeon\s*VII", RegexOptions.IgnoreCase), GpuArchitecture.Vega),
        (new Regex(@"\bRX\s*(4[4-9]0|5[4-9]0)\b", RegexOptions.IgnoreCase), GpuArchitecture.Polaris),
        (new Regex(@"\bR[579]\s*\d{3}|\bHD\s*\d{4}|\bFury\b", RegexOptions.IgnoreCase), GpuArchitecture.Gcn),
    };

    public static bool TryParseDeviceId(string? text, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = Regex.Match(text, @"DEV_([0-9A-Fa-f]{4})");
        string hex = m.Success ? m.Groups[1].Value : text.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
    }

    public static (GpuArchitecture Arch, string Chip) Detect(string? deviceId, string? pnp, string? name)
    {
        if (TryParseDeviceId(deviceId, out int id) || TryParseDeviceId(pnp, out id))
        {
            foreach (var e in Table)
            {
                if (id >= e.Low && id <= e.High)
                {
                    return (e.Arch, e.Chip);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var (pattern, arch) in NameRules)
            {
                if (pattern.IsMatch(name))
                {
                    return (arch, string.Empty);
                }
            }
        }

        return (GpuArchitecture.Unknown, string.Empty);
    }
}
