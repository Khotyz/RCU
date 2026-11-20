using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RedControlUtility.Models;

public sealed class VfCurvePoint : INotifyPropertyChanged
{
    private int _frequencyMHz;
    private int _voltageMv;
    private bool _isLocked;

    public int Index { get; set; }

    public int FrequencyMHz
    {
        get => _frequencyMHz;
        set => Set(ref _frequencyMHz, value);
    }

    public int VoltageMv
    {
        get => _voltageMv;
        set => Set(ref _voltageMv, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => Set(ref _isLocked, value);
    }

    public VfCurvePoint Clone() => new() { Index = Index, FrequencyMHz = FrequencyMHz, VoltageMv = VoltageMv, IsLocked = IsLocked };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref int field, int value, [CallerMemberName] string? name = null)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
