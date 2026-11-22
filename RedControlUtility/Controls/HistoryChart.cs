using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using RedControlUtility.Services;

namespace RedControlUtility.Controls;

public sealed class HistoryChart : Grid
{
    private readonly Polyline _line = new() { StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
    private readonly Canvas _canvas = new() { Height = 64 };
    private readonly TextBlock _title = new() { Opacity = 0.7, FontSize = 12 };
    private readonly TextBlock _value = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly float[] _buffer = new float[MetricsHistory.Capacity];
    private readonly PointCollection _points = new();

    public int Series { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double? FixedMax { get; set; }

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public HistoryChart()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_title);
        SetColumn(_value, 1);
        header.Children.Add(_value);
        Children.Add(header);
        SetRow(_canvas, 1);
        _canvas.Children.Add(_line);
        _line.Points = _points;
        _line.Stroke = new SolidColorBrush(Application.Current.Resources.TryGetValue("SystemAccentColor", out var c) && c is Windows.UI.Color col ? col : Colors.DodgerBlue);
        _canvas.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(14, 128, 128, 128));
        Children.Add(_canvas);
    }

    public void Refresh()
    {
        int n = MetricsHistory.Copy(Series, _buffer);
        _points.Clear();
        if (n == 0)
        {
            _value.Text = string.Empty;
            return;
        }

        double w = _canvas.ActualWidth, h = _canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        double max = FixedMax ?? 0;
        if (FixedMax == null)
        {
            for (int i = 0; i < n; i++) if (_buffer[i] > max) max = _buffer[i];
            max = Math.Max(max * 1.15, 1);
        }

        double step = w / (MetricsHistory.Capacity - 1);
        for (int i = 0; i < n; i++)
        {
            double x = w - (n - 1 - i) * step;
            double y = h - Math.Clamp(_buffer[i] / max, 0, 1) * (h - 2) - 1;
            _points.Add(new Windows.Foundation.Point(x, y));
        }

        _value.Text = $"{_buffer[n - 1]:0} {Unit}";
    }
}
