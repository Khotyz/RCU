using System.Runtime.InteropServices;
using System.Text;

namespace RedControlUtility.Services;

public static class PcieLinkService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid Fmtid;
        public uint Pid;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SpDevinfoData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SpDevinfoData data, uint property, out uint type, byte[] buffer, uint size, out uint required);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SpDevinfoData data, ref DevPropKey key, out uint type, out uint value, uint size, out uint required, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private static readonly Guid PciProps = new("3AB22E31-8264-4B4E-9AF5-A8D2D8E33E62");

    public static string Read(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return string.Empty;
        var cls = DisplayClass;
        IntPtr set = SetupDiGetClassDevsW(ref cls, IntPtr.Zero, IntPtr.Zero, 0x2);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return string.Empty;
        try
        {
            string needle = "DEV_" + deviceId.Trim();
            for (uint i = 0; ; i++)
            {
                var d = new SpDevinfoData { Size = (uint)Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref d)) break;
                var buf = new byte[2048];
                if (!SetupDiGetDeviceRegistryPropertyW(set, ref d, 1, out _, buf, (uint)buf.Length, out _)) continue;
                string ids = Encoding.Unicode.GetString(buf);
                if (!ids.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) || !ids.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;

                uint curSpeed = Get(set, ref d, 9), curWidth = Get(set, ref d, 10), maxSpeed = Get(set, ref d, 11), maxWidth = Get(set, ref d, 12);
                if (maxSpeed == 0 || maxWidth == 0) return string.Empty;
                string max = $"{Gen(maxSpeed)} x{maxWidth}";
                return curSpeed == 0 || curWidth == 0 ? max : $"{Gen(curSpeed)} x{curWidth} / {max}";
            }
        }
        catch
        {
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return string.Empty;
    }

    private static uint Get(IntPtr set, ref SpDevinfoData d, uint pid)
    {
        var key = new DevPropKey { Fmtid = PciProps, Pid = pid };
        return SetupDiGetDevicePropertyW(set, ref d, ref key, out _, out uint v, sizeof(uint), out _, 0) ? v : 0;
    }

    private static string Gen(uint speed) => speed switch
    {
        1 => "1.0",
        2 => "2.0",
        3 => "3.0",
        4 => "4.0",
        5 => "5.0",
        6 => "6.0",
        _ => "?"
    };
}
