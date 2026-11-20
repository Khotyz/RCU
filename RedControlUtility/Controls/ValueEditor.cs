using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RedControlUtility.Controls;

/// <summary>Slider com valor ao lado e botões de valores comuns (ex.: 30, 60, 120 fps). Substitui as caixas numéricas.</summary>
public sealed class ValueEditor : UserControl
{
    private readonly Slider _slider = new() { Width = 220, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { MinWidth = 78, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private string _unit = "";
    private int _suppressDepth;

    public event Action? ValueChanged;

    /// <summary>Formato do texto (ex.: sinal + para potência). Se null usa "valor unidade".</summary>
    public Func<int, string>? Format { get; set; }

    public ValueEditor()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(_slider);
        row.Children.Add(_label);
        row.Children.Add(_chips);
        Content = row;
        _slider.ValueChanged += (_, _) =>
        {
            UpdateLabel();
            if (_suppressDepth == 0) ValueChanged?.Invoke();
        };
    }

    public int Value => (int)Math.Round(_slider.Value);

    public void Configure(int min, int max, int step, string unit, IEnumerable<int>? presets = null)
    {
        if (max <= min) max = min + 1;
        _unit = unit;
        _suppressDepth++;
        try
        {
            _slider.Minimum = min;
            _slider.Maximum = max;
            _slider.StepFrequency = Math.Max(1, step);
            _slider.SmallChange = Math.Max(1, step);
            _slider.LargeChange = Math.Max(1, step) * 5;
        }
        finally
        {
            _suppressDepth--;
        }

        _chips.Children.Clear();
        if (presets != null)
        {
            foreach (int p in presets.Where(p => p >= min && p <= max))
            {
                var b = new Button { Content = Format?.Invoke(p) ?? p.ToString(), Padding = new Thickness(8, 3, 8, 3), MinWidth = 0, FontSize = 12, Tag = p };
                b.Click += (_, _) => _slider.Value = p;
                _chips.Children.Add(b);
            }
        }

        UpdateLabel();
    }

    /// <summary>Define o valor sem disparar <see cref="ValueChanged"/> (usado ao carregar o perfil).</summary>
    public void SetValue(int value)
    {
        _suppressDepth++;
        try
        {
            _slider.Value = Math.Clamp(value, _slider.Minimum, _slider.Maximum);
        }
        finally
        {
            _suppressDepth--;
        }
        UpdateLabel();
    }

    private void UpdateLabel() => _label.Text = Format?.Invoke(Value) ?? $"{Value} {_unit}".Trim();
}
