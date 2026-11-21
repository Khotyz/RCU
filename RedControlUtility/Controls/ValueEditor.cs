using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RedControlUtility.Controls;

public sealed class ValueEditor : UserControl
{
    private readonly Slider _slider = new() { MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { MinWidth = 78, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly VariableSizedWrapGrid _chips = new() { Orientation = Orientation.Horizontal, ItemWidth = 62, ItemHeight = 34, HorizontalAlignment = HorizontalAlignment.Left };
    private string _unit = "";
    private int _suppressDepth;

    public event Action? ValueChanged;

    public Func<int, string>? Format { get; set; }

    public ValueEditor()
    {
        var row = new Grid { ColumnSpacing = 12, RowSpacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 260 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.Children.Add(_slider);
        Grid.SetColumn(_label, 1);
        row.Children.Add(_label);
        Grid.SetRow(_chips, 1);
        Grid.SetColumnSpan(_chips, 3);
        row.Children.Add(_chips);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
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
                var b = new Button { Content = Format?.Invoke(p) ?? p.ToString(), Padding = new Thickness(8, 3, 8, 3), MinWidth = 0, FontSize = 12, Tag = p, Margin = new Thickness(0, 0, 4, 0) };
                b.Click += (_, _) => _slider.Value = p;
                _chips.Children.Add(b);
            }
        }

        UpdateLabel();
    }

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
