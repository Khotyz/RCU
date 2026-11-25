using System.Runtime.InteropServices;
using RedControlUtility.Models;
using RedControlUtility.Services.Adlx;

namespace RedControlUtility.Services.Adl;

public sealed unsafe partial class AdlLegacySession
{
    private const int ClockUnitsPerMhz = 100;
    private const int DefaultMode = 1, AutomaticMode = 2, ManualMode = 3;
    private const int EngineClock = 1 << 0, EngineVoltage = 1 << 2;
    private const int MinimumFan = 1 << 4, TargetFan = 1 << 5;
    private const int MemoryClockFlag = 1 << 1;
    private const int StateClock = 1 << 0, StateVoltage = 1 << 1;
    private const int HeaderSize = 3 * sizeof(int);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRange
    {
        public int Mode, Min, Max, Step, Default;

        public readonly AdlxRange ToRange(int divisor = 1) =>
            new((int)Math.Ceiling((double)Min / divisor), Max / divisor, Math.Max(1, Step / divisor));

        public readonly bool Accepts(int value) => value >= Min && value <= Max;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCapabilities
    {
        public int StateCount, Flags;
        public NativeRange EngineClock, MemoryClock, Voltage, Power, PowerTemperature, FanTemperature,
            FanSpeed, MinimumPerformanceClock, ThrottleNotification, AutoSystemClock;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeState
    {
        public int Clock, Voltage, Enabled, Control;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFan
    {
        public int Mode, ControlMode, CurrentSpeedMode, CurrentSpeed, TargetSpeed, TargetTemperature,
            MinimumPerformanceClock, MinimumSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePower
    {
        public int Mode, Limit, MaxTemperature;
    }

    private delegate* unmanaged[Cdecl]<IntPtr, int, int*, int> _getClocks, _setClocks, _getMemory, _setMemory;
    private delegate* unmanaged[Cdecl]<IntPtr, int, NativePower*, int> _getPower, _setPower;
    private delegate* unmanaged[Cdecl]<IntPtr, int, NativeFan*, int> _getFan, _setFan;
    private NativeCapabilities _overdriveCaps;
    internal TuningCapabilities OverdriveTuning { get; } = new();

    internal static AdlLegacySession? OpenOverdriveN(GpuInfo gpu, int? pciLocation, bool needCore, bool needFan, bool needMemory, bool needPower)
    {
        if ((!needCore && !needFan && !needMemory && !needPower) || gpu.Architecture != GpuArchitecture.Polaris ||
            (!GpuArchitectureDetector.TryParseDeviceId(gpu.DeviceId, out int deviceId) &&
             !GpuArchitectureDetector.TryParseDeviceId(gpu.PnpString, out deviceId))) return null;

        var session = Open(0, out string report, deviceId, pciLocation);
        LogService.Info(report);
        if (session == null) return null;
        try
        {
            if (session.InitializeOverdriveN(needCore, needFan, needMemory, needPower)) return session;
        }
        catch (Exception ex)
        {
            LogService.Error($"AMD Display Library Overdrive N: {ex.Message}");
        }
        session.Dispose();
        return null;
    }

    private bool InitializeOverdriveN(bool needCore, bool needFan, bool needMemory, bool needPower)
    {
        IntPtr Export(string name) => NativeLibrary.TryGetExport(_module, name, out var address) ? address : IntPtr.Zero;
        var overdrive = (delegate* unmanaged[Cdecl]<IntPtr, int, int*, int*, int*, int>)Export("ADL2_Overdrive_Caps");
        var getCaps = (delegate* unmanaged[Cdecl]<IntPtr, int, NativeCapabilities*, int>)Export("ADL2_OverdriveN_CapabilitiesX2_Get");
        int supported = 0, enabled = 0, version = 0;
        NativeCapabilities caps = default;
        if (overdrive == null || getCaps == null || overdrive(_ctx, _adapter, &supported, &enabled, &version) != 0 ||
            supported == 0 || version < 7 || getCaps(_ctx, _adapter, &caps) != 0) return false;

        _overdriveCaps = caps;
        var tuning = OverdriveTuning;
        if (needCore && (caps.Flags & (EngineClock | EngineVoltage)) == (EngineClock | EngineVoltage) && caps.StateCount is > 0 and <= 32)
        {
            _getClocks = (delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)Export("ADL2_OverdriveN_SystemClocksX2_Get");
            _setClocks = (delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)Export("ADL2_OverdriveN_SystemClocksX2_Set");
            var freq = caps.EngineClock.ToRange(ClockUnitsPerMhz);
            var volt = caps.Voltage.ToRange();
            if (_getClocks != null && _setClocks != null && freq.IsValid && freq.Min > 0 && volt.IsValid && volt.Min > 0 &&
                ReadStates() is { Count: > 0 })
            {
                tuning.GfxMode = GfxTuningMode.StateList;
                tuning.StateFreqRange = freq;
                tuning.StateVoltageRange = volt;
            }
        }

        if (needMemory && (caps.Flags & MemoryClockFlag) != 0 && caps.StateCount is > 0 and <= 32)
        {
            _getMemory = (delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)Export("ADL2_OverdriveN_MemoryClocksX2_Get");
            _setMemory = (delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)Export("ADL2_OverdriveN_MemoryClocksX2_Set");
            var memory = caps.MemoryClock.ToRange(ClockUnitsPerMhz);
            if (_getMemory != null && _setMemory != null && memory.IsValid && memory.Min > 0 && ReadMemoryClock() is > 0)
            {
                tuning.VramSupported = true;
                tuning.VramFreqSupported = true;
                tuning.VramFreqRange = memory;
            }
        }

        if (needPower)
        {
            _getPower = (delegate* unmanaged[Cdecl]<IntPtr, int, NativePower*, int>)Export("ADL2_OverdriveN_PowerLimit_Get");
            _setPower = (delegate* unmanaged[Cdecl]<IntPtr, int, NativePower*, int>)Export("ADL2_OverdriveN_PowerLimit_Set");
            NativePower power = default;
            var range = caps.Power.ToRange();
            if (_getPower != null && _setPower != null && range.IsValid && range.Max > range.Min && _getPower(_ctx, _adapter, &power) == 0 &&
                caps.Power.Accepts(power.Limit))
            {
                tuning.PowerSupported = true;
                tuning.PowerRange = range;
                tuning.PowerDefault = caps.Power.Default;
            }
        }

        if (needFan && (caps.Flags & (MinimumFan | TargetFan)) != 0)
        {
            _getFan = (delegate* unmanaged[Cdecl]<IntPtr, int, NativeFan*, int>)Export("ADL2_OverdriveN_FanControl_Get");
            _setFan = (delegate* unmanaged[Cdecl]<IntPtr, int, NativeFan*, int>)Export("ADL2_OverdriveN_FanControl_Set");
            NativeFan fan = default;
            var range = caps.FanSpeed.ToRange();
            if (_getFan != null && _setFan != null && range.IsValid && range.Min >= 0 && _getFan(_ctx, _adapter, &fan) == 0)
            {
                if ((caps.Flags & MinimumFan) != 0 && caps.FanSpeed.Accepts(fan.MinimumSpeed)) tuning.FanMinRpmRange = range;
                if ((caps.Flags & TargetFan) != 0 && caps.FanSpeed.Accepts(fan.TargetSpeed)) tuning.FanTargetRpmRange = range;
                tuning.FanSupported = tuning.FanMinRpmRange.IsValid || tuning.FanTargetRpmRange.IsValid;
            }
        }

        return tuning.AnyTuning;
    }

    private bool GetMemory(int* buffer, int count)
    {
        buffer[0] = HeaderSize + count * sizeof(NativeState);
        buffer[2] = count;
        return _getMemory(_ctx, _adapter, buffer) == 0 && buffer[2] == count;
    }

    private static int TopEnabled(NativeState* states, int count)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            if (states[i].Enabled > 0 && states[i].Clock > 0) return i;
        }

        return -1;
    }

    private int ReadMemoryClock()
    {
        int count = _overdriveCaps.StateCount;
        int* buffer = stackalloc int[3 + count * 4];
        new Span<int>(buffer, 3 + count * 4).Clear();
        if (!GetMemory(buffer, count)) return 0;
        var states = (NativeState*)(buffer + 3);
        int top = TopEnabled(states, count);
        return top < 0 ? 0 : states[top].Clock / ClockUnitsPerMhz;
    }

    private AdlxResult ApplyMemoryClock(int mhz)
    {
        int count = _overdriveCaps.StateCount;
        int* buffer = stackalloc int[3 + count * 4];
        new Span<int>(buffer, 3 + count * 4).Clear();
        if (!GetMemory(buffer, count)) return AdlxResult.Fail;
        var states = (NativeState*)(buffer + 3);
        int top = TopEnabled(states, count);
        if (top < 0) return AdlxResult.Fail;
        int clock = mhz * ClockUnitsPerMhz;
        if (!_overdriveCaps.MemoryClock.Accepts(clock)) return AdlxResult.InvalidArgs;
        if (clock == states[top].Clock) return AdlxResult.Ok;
        for (int i = 0; i < top; i++)
        {
            if (states[i].Enabled > 0 && states[i].Clock > clock) return AdlxResult.InvalidArgs;
        }

        states[top].Clock = clock;
        buffer[1] = ManualMode;
        return Result(_setMemory(_ctx, _adapter, buffer), "MemoryClocksX2_Set");
    }

    private AdlxResult ApplyPowerLimit(int percent)
    {
        NativePower power = default;
        if (_getPower(_ctx, _adapter, &power) != 0) return AdlxResult.Fail;
        if (!_overdriveCaps.Power.Accepts(percent)) return AdlxResult.InvalidArgs;
        if (power.Limit == percent && power.Mode == ManualMode) return AdlxResult.Ok;
        power.Mode = ManualMode;
        power.Limit = percent;
        return Result(_setPower(_ctx, _adapter, &power), "PowerLimit_Set");
    }

    private bool GetClocks(int* buffer, int count)
    {
        buffer[0] = HeaderSize + count * sizeof(NativeState);
        buffer[2] = count;
        return _getClocks(_ctx, _adapter, buffer) == 0 && buffer[2] == count;
    }

    private static bool IsEditable(NativeState state) => state.Enabled > 0 &&
        (state.Control & (StateClock | StateVoltage)) == (StateClock | StateVoltage);

    private List<VfCurvePoint>? ReadStates()
    {
        int count = _overdriveCaps.StateCount;
        int* buffer = stackalloc int[3 + count * 4];
        new Span<int>(buffer, 3 + count * 4).Clear();
        if (!GetClocks(buffer, count)) return null;
        var states = (NativeState*)(buffer + 3);
        var result = new List<VfCurvePoint>();
        for (int i = 0; i < count; i++)
        {
            var state = states[i];
            if (IsEditable(state) && state.Clock > 0 && _overdriveCaps.Voltage.Accepts(state.Voltage))
                result.Add(new VfCurvePoint { Index = i, FrequencyMHz = state.Clock / ClockUnitsPerMhz, VoltageMv = state.Voltage });
        }
        return result;
    }

    internal void ReadOverdriveTuning(TuningSettings settings)
    {
        if (OverdriveTuning.GfxMode == GfxTuningMode.StateList) settings.VfStates = ReadStates();
        if (OverdriveTuning.VramFreqSupported && ReadMemoryClock() is > 0 and var memory) settings.VramMaxMhz = memory;
        if (OverdriveTuning.PowerSupported)
        {
            NativePower power = default;
            if (_getPower(_ctx, _adapter, &power) == 0) settings.PowerLimitPercent = power.Limit;
        }

        if (!OverdriveTuning.FanSupported) return;
        NativeFan fan = default;
        if (_getFan(_ctx, _adapter, &fan) != 0) return;
        if (OverdriveTuning.FanMinRpmRange.IsValid) settings.FanMinRpm = fan.MinimumSpeed;
        if (OverdriveTuning.FanTargetRpmRange.IsValid) settings.FanTargetRpm = fan.TargetSpeed;
    }

    internal void ApplyOverdriveTuning(TuningSettings settings, ApplyReport report)
    {
        if (OverdriveTuning.GfxMode == GfxTuningMode.StateList && settings.VfStates is { Count: > 0 } states)
            report.Add(UiHelper.L("Report.VfStates"), ApplyStates(states));
        if (OverdriveTuning.VramFreqSupported && settings.VramMaxMhz != null)
            report.Add(UiHelper.L("Report.VramClock"), ApplyMemoryClock(settings.VramMaxMhz.Value));
        if (OverdriveTuning.PowerSupported && settings.PowerLimitPercent != null)
            report.Add(UiHelper.L("Report.PowerLimit"), ApplyPowerLimit(settings.PowerLimitPercent.Value));
        if (OverdriveTuning.FanSupported && (settings.FanMinRpm != null || settings.FanTargetRpm != null))
            report.Add(UiHelper.L("Report.Fans"), ApplyFanSpeeds(settings));
    }

    private AdlxResult ApplyStates(List<VfCurvePoint> points)
    {
        int count = _overdriveCaps.StateCount;
        int* buffer = stackalloc int[3 + count * 4];
        new Span<int>(buffer, 3 + count * 4).Clear();
        if (!GetClocks(buffer, count)) return AdlxResult.Fail;
        var states = (NativeState*)(buffer + 3);
        var indices = new HashSet<int>();
        bool changed = false;
        foreach (var point in points)
        {
            int i = point.Index;
            if (i < 0 || i >= count || !indices.Add(i) || !IsEditable(states[i])) return AdlxResult.InvalidArgs;
            bool clockChanged = point.FrequencyMHz != states[i].Clock / ClockUnitsPerMhz;
            bool voltageChanged = point.VoltageMv != states[i].Voltage;
            if (clockChanged)
            {
                if (point.FrequencyMHz <= 0 || point.FrequencyMHz > int.MaxValue / ClockUnitsPerMhz) return AdlxResult.InvalidArgs;
                int clock = point.FrequencyMHz * ClockUnitsPerMhz;
                if (!_overdriveCaps.EngineClock.Accepts(clock)) return AdlxResult.InvalidArgs;
                states[i].Clock = clock;
            }
            if (voltageChanged)
            {
                if (!_overdriveCaps.Voltage.Accepts(point.VoltageMv)) return AdlxResult.InvalidArgs;
                states[i].Voltage = point.VoltageMv;
            }
            changed |= clockChanged || voltageChanged;
        }
        if (!changed) return AdlxResult.Ok;

        int lastClock = 0;
        for (int i = 0; i < count; i++)
        {
            states[i].Enabled = states[i].Enabled > 0 ? 1 : 0;
            if (states[i].Enabled == 0) continue;
            if (states[i].Clock < lastClock) return AdlxResult.InvalidArgs;
            lastClock = states[i].Clock;
        }
        buffer[1] = ManualMode;
        return Result(_setClocks(_ctx, _adapter, buffer), "SystemClocksX2_Set");
    }

    private AdlxResult ApplyFanSpeeds(TuningSettings settings)
    {
        NativeFan fan = default;
        if (_getFan(_ctx, _adapter, &fan) != 0) return AdlxResult.Fail;
        int min = settings.FanMinRpm ?? fan.MinimumSpeed;
        int target = settings.FanTargetRpm ?? fan.TargetSpeed;
        if ((settings.FanMinRpm != null && (!OverdriveTuning.FanMinRpmRange.IsValid || !_overdriveCaps.FanSpeed.Accepts(min))) ||
            (settings.FanTargetRpm != null && (!OverdriveTuning.FanTargetRpmRange.IsValid || !_overdriveCaps.FanSpeed.Accepts(target))) ||
            (target > 0 && min > target)) return AdlxResult.InvalidArgs;
        if (min == fan.MinimumSpeed && target == fan.TargetSpeed && fan.Mode == ManualMode) return AdlxResult.Ok;
        fan.Mode = ManualMode;
        fan.ControlMode = ManualMode;
        fan.MinimumSpeed = min;
        fan.TargetSpeed = target;
        return Result(_setFan(_ctx, _adapter, &fan), "FanControl_Set");
    }

    internal AdlxResult ResetOverdriveTuning()
    {
        var result = AdlxResult.Ok;
        if (OverdriveTuning.GfxMode == GfxTuningMode.StateList)
        {
            int count = _overdriveCaps.StateCount;
            int* buffer = stackalloc int[3 + count * 4];
            new Span<int>(buffer, 3 + count * 4).Clear();
            buffer[1] = DefaultMode;
            if (!GetClocks(buffer, count)) result = AdlxResult.Fail;
            else
            {
                buffer[1] = DefaultMode;
                result = Result(_setClocks(_ctx, _adapter, buffer), "SystemClocksX2_Set defaults");
            }
        }
        if (OverdriveTuning.VramFreqSupported)
        {
            int count = _overdriveCaps.StateCount;
            int* buffer = stackalloc int[3 + count * 4];
            new Span<int>(buffer, 3 + count * 4).Clear();
            if (GetMemory(buffer, count))
            {
                buffer[1] = DefaultMode;
                var memoryResult = Result(_setMemory(_ctx, _adapter, buffer), "MemoryClocksX2_Set defaults");
                if (!memoryResult.Succeeded()) result = memoryResult;
            }
        }

        if (OverdriveTuning.PowerSupported)
        {
            NativePower power = default;
            if (_getPower(_ctx, _adapter, &power) == 0)
            {
                power.Mode = DefaultMode;
                power.Limit = _overdriveCaps.Power.Default;
                var powerResult = Result(_setPower(_ctx, _adapter, &power), "PowerLimit_Set defaults");
                if (!powerResult.Succeeded()) result = powerResult;
            }
        }

        if (OverdriveTuning.FanSupported)
        {
            NativeFan fan = default;
            var fanResult = AdlxResult.Fail;
            if (_getFan(_ctx, _adapter, &fan) == 0)
            {
                fan.Mode = AutomaticMode;
                fan.ControlMode = AutomaticMode;
                fan.CurrentSpeedMode = AutomaticMode;
                fan.TargetSpeed = 0;
                fanResult = Result(_setFan(_ctx, _adapter, &fan), "FanControl_Set automatic");
            }
            if (!fanResult.Succeeded()) result = fanResult;
        }
        return result;
    }

    private static AdlxResult Result(int code, string operation)
    {
        LogService.Info($"AMD Display Library Overdrive N: {operation} => {code}");
        return code == 0 ? AdlxResult.Ok : AdlxResult.Fail;
    }
}
