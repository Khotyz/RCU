using Microsoft.Win32;
using RedControlUtility.Models;

namespace RedControlUtility.Services;

public static class DriverInfoService
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public sealed class RegistryGpu
    {
        public string Name { get; set; } = string.Empty;
        public string MatchingId { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public long VramBytes { get; set; }
    }

    public static List<RegistryGpu> ReadAmdAdapters()
    {
        var list = new List<RegistryGpu>();
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(DisplayClass);
            if (baseKey == null) return list;

            foreach (string sub in baseKey.GetSubKeyNames())
            {
                if (!int.TryParse(sub, out _)) continue;
                using var k = baseKey.OpenSubKey(sub);
                if (k == null) continue;

                string provider = k.GetValue("ProviderName") as string ?? string.Empty;
                string desc = k.GetValue("DriverDesc") as string ?? string.Empty;
                string match = k.GetValue("MatchingDeviceId") as string ?? string.Empty;
                bool isAmd = provider.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) ||
                             provider.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                             match.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase);
                if (!isAmd || string.IsNullOrWhiteSpace(desc)) continue;

                long vram = 0;
                object? mem = k.GetValue("HardwareInformation.qwMemorySize");
                if (mem is long l) vram = l;
                else if (mem is byte[] b && b.Length >= 8) vram = BitConverter.ToInt64(b, 0);
                else if (k.GetValue("HardwareInformation.MemorySize") is byte[] b4 && b4.Length >= 4) vram = BitConverter.ToUInt32(b4, 0);

                list.Add(new RegistryGpu
                {
                    Name = desc,
                    MatchingId = match,
                    DriverVersion = k.GetValue("DriverVersion") as string ?? string.Empty,
                    VramBytes = vram
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"Registro (adaptadores AMD): {ex.Message}");
        }

        return list;
    }

    public static void Enrich(GpuInfo gpu)
    {
        var adapters = ReadAmdAdapters();
        if (adapters.Count == 0) return;

        RegistryGpu? match = null;
        if (GpuArchitectureDetector.TryParseDeviceId(gpu.DeviceId, out int id))
        {
            match = adapters.FirstOrDefault(a => a.MatchingId.Contains($"DEV_{id:X4}", StringComparison.OrdinalIgnoreCase));
        }

        match ??= adapters.FirstOrDefault(a => string.Equals(a.Name, gpu.Name, StringComparison.OrdinalIgnoreCase));
        match ??= adapters.Count == 1 ? adapters[0] : null;
        if (match == null) return;

        if (string.IsNullOrEmpty(gpu.DriverVersion)) gpu.DriverVersion = match.DriverVersion;
        if (gpu.VramMb <= 0 && match.VramBytes > 0) gpu.VramMb = (int)(match.VramBytes / (1024 * 1024));
    }
}
