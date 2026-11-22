using System.Runtime.InteropServices;

namespace RedControlUtility.Services;

public sealed unsafe class DisplayColorService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool SetDeviceGammaRamp(IntPtr hDC, ushort* ramp);

    [DllImport("gdi32.dll")]
    private static extern bool GetDeviceGammaRamp(IntPtr hDC, ushort* ramp);

    public bool ApplyColorProfile(string profileName, bool onlyIfChanged = false)
    {
        double rGain = 1.0;
        double gGain = 1.0;
        double bGain = 1.0;
        double gamma = 1.0;
        double blackPoint = 0.0;

        switch (profileName)
        {
            case "Vivid":
                gamma = 0.88;
                rGain = 1.05;
                gGain = 1.02;
                bGain = 1.05;
                break;
            case "Cooler":
                rGain = 0.90;
                gGain = 0.96;
                bGain = 1.08;
                break;
            case "Warmer":
                rGain = 1.08;
                gGain = 0.96;
                bGain = 0.84;
                break;
            case "FakeHdr":
                gamma = 0.90;
                rGain = 1.03;
                gGain = 1.01;
                bGain = 1.03;
                break;
            case "FakeOledIps":
                gamma = 1.07;
                blackPoint = 0.028;
                rGain = 1.00;
                gGain = 1.00;
                bGain = 0.97;
                break;
            case "FakeOledVa":
                gamma = 0.96;
                blackPoint = 0.006;
                rGain = 1.00;
                gGain = 0.985;
                bGain = 1.00;
                break;
            default:
                break;
        }

        return ApplyCustomColor(gamma, rGain, gGain, bGain, blackPoint, onlyIfChanged);
    }

    public bool ApplyCustomColor(double gamma, double rGain, double gGain, double bGain, double blackPoint = 0.0, bool onlyIfChanged = false)
    {
        ushort* ramp = stackalloc ushort[768];
        for (int i = 0; i < 256; i++)
        {
            double normalized = i / 255.0;
            double curved = Math.Pow(normalized, gamma);
            if (blackPoint > 0) curved = Math.Max(0.0, (curved - blackPoint) / (1.0 - blackPoint));

            ramp[i] = (ushort)Math.Clamp(curved * rGain * 65535.0, 0, 65535);
            ramp[256 + i] = (ushort)Math.Clamp(curved * gGain * 65535.0, 0, 65535);
            ramp[512 + i] = (ushort)Math.Clamp(curved * bGain * 65535.0, 0, 65535);
        }

        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (onlyIfChanged)
            {
                ushort* current = stackalloc ushort[768];
                if (GetDeviceGammaRamp(hdc, current) && new ReadOnlySpan<ushort>(current, 768).SequenceEqual(new ReadOnlySpan<ushort>(ramp, 768)))
                {
                    return true;
                }
            }

            return SetDeviceGammaRamp(hdc, ramp);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }
}
