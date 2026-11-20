using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RedControlUtility.Models;

public sealed class FanCurvePoint : INotifyPropertyChanged
{
    private int _temperatureC;
    private int _fanSpeedPercent;

    public int TemperatureC
    {
        get => _temperatureC;
        set { if (_temperatureC != value) { _temperatureC = value; OnChanged(); } }
    }

    public int FanSpeedPercent
    {
        get => _fanSpeedPercent;
        set { if (_fanSpeedPercent != value) { _fanSpeedPercent = value; OnChanged(); } }
    }

    public FanCurvePoint Clone() => new() { TemperatureC = TemperatureC, FanSpeedPercent = FanSpeedPercent };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
