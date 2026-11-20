using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RedControlUtility.Models;

public sealed class StatTile : INotifyPropertyChanged
{
    private string _value = "—";
    private double _fraction;

    public string TitleKey { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string TipKey { get; init; } = string.Empty;

    /// <summary>Opcional: some da tela quando o sensor não existe nesta GPU (em vez de mostrar um traço).</summary>
    public bool Optional { get; init; }
    public bool HasValue { get; set; } = true;

    private string _tip = string.Empty;
    public string Tip { get => _tip; set => Set(ref _tip, value); }
    public string Glyph { get; init; } = "";

    private string _title = string.Empty;
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Value { get => _value; set => Set(ref _value, value); }

    /// <summary>0..100 para a barra de progresso.</summary>
    public double Fraction { get => _fraction; set => Set(ref _fraction, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
