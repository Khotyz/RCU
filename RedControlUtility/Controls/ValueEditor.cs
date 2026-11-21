using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RedControlUtility.Controls;

public sealed class ValueEditor : UserControl
{
    private readonly Slider _slider = new() { MinWidth = 120, MaxWidth = 240, Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { MinWidth = 64, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly Button _presets = new() { Width = 28, Height = 28, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Content = new FontIcon { Glyph = "\uE70D", FontSize = 10 } };
    private readonly MenuFlyout _menu = new();
    private string _unit = "";
    private int _suppressDepth;

    public event Action? ValueChanged;

    public Func<int, string>? Format { get; set; }

    public ValueEditor()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(_slider);
        row.Children.Add(_label);
        row.Children.Add(_presets);
        _presets.Flyout = _menu;
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

        _menu.Items.Clear();
        if (presets != null)
        {
            foreach (int p in presets.Where(p => p >= min && p <= max))
            {
                var item = new MenuFlyoutItem { Text = Format?.Invoke(p) ?? $"{p} {_unit}".Trim() };
                int value = p;
                item.Click += (_, _) => _slider.Value = value;
                _menu.Items.Add(item);
            }
        }

        _presets.Visibility = _menu.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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
