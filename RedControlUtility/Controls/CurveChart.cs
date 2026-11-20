using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace RedControlUtility.Controls;

/// <summary>
/// Gráfico de curva leve (V/F, ventoinha). Os elementos visuais são criados uma única vez e apenas
/// reposicionados; os redesenhos são agrupados (coalescidos) num timer de ~30 ms. Isso elimina o lag
/// que existia no controle antigo (que recriava dezenas de shapes e re-ligava ItemsSource a cada passo do slider).
/// </summary>
public sealed class CurveChart : Canvas
{
    private const double PadL = 44, PadR = 14, PadT = 12, PadB = 26;

    /// <summary>Pincel do ResourceDictionary do app (App.xaml); cai na cor padrão se não existir.</summary>
    private static SolidColorBrush ThemeBrush(string key, Color fallback)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(key, out object? o) == true && o is SolidColorBrush b) return b;
        }
        catch
        {
        }

        return new SolidColorBrush(fallback);
    }

    private readonly Polyline _line = new()
    {
        StrokeThickness = 2.5,
        Stroke = ThemeBrush("CurveLineBrush", Color.FromArgb(255, 255, 90, 95)),
        StrokeLineJoin = PenLineJoin.Round
    };

    // "Fantasma": curva de referência (último estado aplicado) para comparar com a edição atual.
    private readonly Polyline _ghost = new()
    {
        StrokeThickness = 2,
        Stroke = ThemeBrush("CurveGhostBrush", Color.FromArgb(150, 175, 175, 190)),
        StrokeDashArray = new DoubleCollection { 4, 4 },
        StrokeLineJoin = PenLineJoin.Round
    };

    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _marquee = new()
    {
        Stroke = ThemeBrush("CurveMarqueeStrokeBrush", Color.FromArgb(220, 255, 90, 95)),
        StrokeThickness = 1,
        Fill = ThemeBrush("CurveMarqueeBrush", Color.FromArgb(40, 255, 90, 95)),
        Visibility = Visibility.Collapsed
    };

    private readonly HashSet<int> _sel = new();
    private List<(double X, double Y)>? _ghostPts;
    private bool _marqueeActive, _moving;
    private Point _mqStart;
    private double _lastY, _accum;

    /// <summary>Modo estilo MSI Afterburner: seleção por retângulo/clique e deslocamento vertical (arrastar, scroll, setas).</summary>
    public bool Selectable { get; set; }

    /// <summary>Passo do scroll e das setas, em unidades do eixo Y.</summary>
    public double Step { get; set; } = 5;

    /// <summary>Pontos selecionados e quanto (em unidades de Y, positivo = para cima) deslocar.</summary>
    public event Action<IReadOnlyList<int>, double>? SelectionShifted;

    /// <summary>Arrastar UM ponto sozinho move livremente (X e Y), de forma manual. Só vale para índices aceitos por <see cref="CanMoveFreely"/>.</summary>
    public event Action<int, double, double>? PointMoved;
    public Func<int, bool>? CanMoveFreely { get; set; }
    private int _freeIndex = -1;

    private readonly List<Ellipse> _dots = new();
    private readonly List<Line> _grid = new();
    private readonly TextBlock _yMaxText = Label();
    private readonly TextBlock _yMinText = Label();
    private readonly TextBlock _xMinText = Label();
    private readonly TextBlock _xMaxText = Label();
    private readonly List<(double X, double Y)> _points = new();
    private DispatcherQueueTimer? _timer;
    private int _dragIndex = -1;

    /// <summary>Permite arrastrar os pontos com o mouse. O evento informa o índice e a posição em unidades dos eixos.</summary>
    public bool Draggable { get; set; }
    public event Action<int, double, double>? PointDragged;

    private double _xMin, _xMax = 100, _yMin, _yMax = 100;
    private string _xUnit = string.Empty, _yUnit = string.Empty;

    public CurveChart()
    {
        Height = 230;
        MinWidth = 320;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "Curve editor");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(this, UiHelper.L("Tip.CurveChart"));
        Background = new SolidColorBrush(Colors.Transparent);

        for (int i = 0; i < 10; i++)
        {
            var l = new Line { Stroke = ThemeBrush("CurveGridBrush", Color.FromArgb(38, 255, 255, 255)), StrokeThickness = 1 };
            _grid.Add(l);
            Children.Add(l);
        }

        Children.Add(_ghost);
        Children.Add(_line);
        Children.Add(_marquee);
        Children.Add(_yMaxText);
        Children.Add(_yMinText);
        Children.Add(_xMinText);
        Children.Add(_xMaxText);

        SizeChanged += (_, _) => Redraw();
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => { _dragIndex = -1; EndGesture(); };
        PointerWheelChanged += OnWheel;
        IsTabStop = true;
        KeyDown += OnKey;
    }

    private int HitTest(Point pos)
    {
        int best = -1;
        double bestD = 28 * 28;
        for (int i = 0; i < _points.Count; i++)
        {
            double px = PadL + (_points[i].X - _xMin) / (_xMax - _xMin) * Pw;
            double py = PadT + Ph - (_points[i].Y - _yMin) / (_yMax - _yMin) * Ph;
            double d = (px - pos.X) * (px - pos.X) + (py - pos.Y) * (py - pos.Y);
            if (d < bestD) { bestD = d; best = i; }
        }

        return best;
    }

    private static bool CtrlDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static bool ShiftDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void EndGesture()
    {
        _moving = false;
        _freeIndex = -1;
        _marqueeActive = false;
        _accum = 0;
        _marquee.Visibility = Visibility.Collapsed;
    }

    private void Shift(double dy)
    {
        if (_sel.Count == 0 || dy == 0) return;
        SelectionShifted?.Invoke(_sel.OrderBy(i => i).ToList(), dy);
    }

    private void OnWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!Selectable || _sel.Count == 0) return;
        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta == 0) return;
        Shift(Math.Sign(delta) * (ShiftDown() ? 1 : Step));
        e.Handled = true;
    }

    private void OnKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!Selectable) return;
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Up:
                Shift(ShiftDown() ? 1 : Step);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Down:
                Shift(-(ShiftDown() ? 1 : Step));
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Escape:
                _sel.Clear();
                RequestRedraw();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.A when CtrlDown():
                _sel.Clear();
                for (int i = 0; i < _points.Count; i++) _sel.Add(i);
                RequestRedraw();
                e.Handled = true;
                break;
        }
    }

    public void SelectAll()
    {
        _sel.Clear();
        for (int i = 0; i < _points.Count; i++) _sel.Add(i);
        RequestRedraw();
    }

    public void SetGhost(IEnumerable<(double X, double Y)>? points)
    {
        _ghostPts = points?.ToList();
        RequestRedraw();
    }

    private double Pw => ActualWidth - PadL - PadR;
    private double Ph => ActualHeight - PadT - PadB;

    private void OnPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_points.Count == 0 || Pw < 20 || Ph < 20) return;
        var pos = e.GetCurrentPoint(this).Position;
        int best = HitTest(pos);

        if (Selectable)
        {
            Focus(FocusState.Pointer);
            bool ctrl = CtrlDown();
            if (best >= 0)
            {
                if (_sel.Contains(best) && ctrl)
                {
                    _sel.Remove(best);
                    RequestRedraw();
                    e.Handled = true;
                    return;
                }

                if (!_sel.Contains(best))
                {
                    if (!ctrl) _sel.Clear();
                    _sel.Add(best);
                }

                _moving = true;
                _lastY = pos.Y;
                _accum = 0;
                // Um único ponto selecionado: edição manual livre. Vários pontos: deslocamento linear do grupo.
                _freeIndex = _sel.Count == 1 && _sel.Contains(best) && (CanMoveFreely?.Invoke(best) ?? false) ? best : -1;
            }
            else
            {
                if (!ctrl) _sel.Clear();
                _marqueeActive = true;
                _mqStart = pos;
                SetLeft(_marquee, pos.X);
                SetTop(_marquee, pos.Y);
                _marquee.Width = 0;
                _marquee.Height = 0;
                _marquee.Visibility = Visibility.Visible;
            }

            CapturePointer(e.Pointer);
            RequestRedraw();
            e.Handled = true;
            return;
        }

        if (!Draggable) return;
        if (best < 0) return;
        _dragIndex = best;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (Selectable)
        {
            var mp = e.GetCurrentPoint(this).Position;
            if (_moving && _freeIndex >= 0)
            {
                double fx = Math.Clamp(_xMin + (mp.X - PadL) / Pw * (_xMax - _xMin), _xMin, _xMax);
                double fy = Math.Clamp(_yMin + (PadT + Ph - mp.Y) / Ph * (_yMax - _yMin), _yMin, _yMax);
                PointMoved?.Invoke(_freeIndex, fx, fy);
            }
            else if (_moving)
            {
                _accum += (_lastY - mp.Y) / Ph * (_yMax - _yMin);
                _lastY = mp.Y;
                double whole = Math.Truncate(_accum);
                if (whole != 0)
                {
                    _accum -= whole;
                    Shift(whole);
                }
            }
            else if (_marqueeActive)
            {
                double l = Math.Min(_mqStart.X, mp.X), t = Math.Min(_mqStart.Y, mp.Y);
                double w = Math.Abs(mp.X - _mqStart.X), h = Math.Abs(mp.Y - _mqStart.Y);
                SetLeft(_marquee, l);
                SetTop(_marquee, t);
                _marquee.Width = w;
                _marquee.Height = h;
                if (!CtrlDown()) _sel.Clear();
                for (int i = 0; i < _points.Count; i++)
                {
                    double px = PadL + (_points[i].X - _xMin) / (_xMax - _xMin) * Pw;
                    double py = PadT + Ph - (_points[i].Y - _yMin) / (_yMax - _yMin) * Ph;
                    if (px >= l && px <= l + w && py >= t && py <= t + h) _sel.Add(i);
                }

                RequestRedraw();
            }

            e.Handled = true;
            return;
        }

        if (_dragIndex < 0) return;
        var pos = e.GetCurrentPoint(this).Position;
        double x = _xMin + (pos.X - PadL) / Pw * (_xMax - _xMin);
        double y = _yMin + (PadT + Ph - pos.Y) / Ph * (_yMax - _yMin);
        x = Math.Clamp(x, _xMin, _xMax);
        y = Math.Clamp(y, _yMin, _yMax);
        PointDragged?.Invoke(_dragIndex, x, y);
        e.Handled = true;
    }

    private void OnReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (Selectable)
        {
            EndGesture();
            ReleasePointerCaptures();
            return;
        }

        if (_dragIndex < 0) return;
        _dragIndex = -1;
        ReleasePointerCaptures();
    }

    private static readonly SolidColorBrush NormalFill = ThemeBrush("CurvePointFillBrush", Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush SelectedFill = ThemeBrush("CurvePointStrokeBrush", Color.FromArgb(255, 229, 37, 42));

    private static TextBlock Label() => new() { FontSize = 11, Opacity = 0.7 };

    public void SetAxes(double xMin, double xMax, double yMin, double yMax, string xUnit, string yUnit)
    {
        _xMin = xMin; _xMax = Math.Max(xMax, xMin + 1);
        _yMin = yMin; _yMax = Math.Max(yMax, yMin + 1);
        _xUnit = xUnit; _yUnit = yUnit;
        RequestRedraw();
    }

    public void SetPoints(IEnumerable<(double X, double Y)> points)
    {
        _points.Clear();
        _points.AddRange(points);
        _sel.RemoveWhere(i => i >= _points.Count);
        RequestRedraw();
    }

    public void RequestRedraw()
    {
        if (_timer == null)
        {
            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(30);
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) =>
            {
                try
                {
                    Redraw();
                }
                catch (Exception ex)
                {
                    RedControlUtility.Services.LogService.Error($"CurveChart.Redraw: {ex.Message}");
                }
            };
        }

        if (!_timer.IsRunning) _timer.Start();
    }

    private void Redraw()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 100 || h < 80) return;

        double pw = w - PadL - PadR, ph = h - PadT - PadB;

        for (int i = 0; i < 5; i++)
        {
            double y = PadT + ph * i / 4.0;
            var hl = _grid[i];
            hl.X1 = PadL; hl.X2 = PadL + pw; hl.Y1 = hl.Y2 = y;

            double x = PadL + pw * i / 4.0;
            var vl = _grid[5 + i];
            vl.X1 = vl.X2 = x; vl.Y1 = PadT; vl.Y2 = PadT + ph;
        }

        Place(_yMaxText, $"{_yMax:0}{_yUnit}", 2, PadT - 6);
        Place(_yMinText, $"{_yMin:0}{_yUnit}", 2, PadT + ph - 8);
        Place(_xMinText, $"{_xMin:0}{_xUnit}", PadL, PadT + ph + 6);
        Place(_xMaxText, $"{_xMax:0}{_xUnit}", PadL + pw - 36, PadT + ph + 6);

        var pts = new PointCollection();
        for (int i = 0; i < _points.Count; i++)
        {
            double px = PadL + (_points[i].X - _xMin) / (_xMax - _xMin) * pw;
            double py = PadT + ph - (_points[i].Y - _yMin) / (_yMax - _yMin) * ph;
            px = Math.Clamp(px, PadL, PadL + pw);
            py = Math.Clamp(py, PadT, PadT + ph);
            pts.Add(new Point(px, py));

            Ellipse dot;
            if (i < _dots.Count)
            {
                dot = _dots[i];
            }
            else
            {
                dot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = ThemeBrush("CurvePointFillBrush", Color.FromArgb(255, 255, 255, 255)),
                    Stroke = ThemeBrush("CurvePointStrokeBrush", Color.FromArgb(255, 229, 37, 42)),
                    StrokeThickness = 2
                };
                _dots.Add(dot);
                Children.Add(dot);
            }

            SetLeft(dot, px - 5);
            SetTop(dot, py - 5);
        }

        _line.Points = pts;

        var gp = new PointCollection();
        if (_ghostPts != null)
        {
            foreach (var g in _ghostPts)
            {
                double gx = Math.Clamp(PadL + (g.X - _xMin) / (_xMax - _xMin) * pw, PadL, PadL + pw);
                double gy = Math.Clamp(PadT + ph - (g.Y - _yMin) / (_yMax - _yMin) * ph, PadT, PadT + ph);
                gp.Add(new Point(gx, gy));
            }
        }

        _ghost.Points = gp;

        for (int i = 0; i < _dots.Count && i < _points.Count; i++)
        {
            bool on = _sel.Contains(i);
            _dots[i].Fill = on ? SelectedFill : NormalFill;
            _dots[i].Width = _dots[i].Height = on ? 13 : 10;
            SetLeft(_dots[i], GetLeft(_dots[i]) + (on ? -1.5 : 0));
            SetTop(_dots[i], GetTop(_dots[i]) + (on ? -1.5 : 0));
        }

        while (_dots.Count > _points.Count)
        {
            var d = _dots[^1];
            _dots.RemoveAt(_dots.Count - 1);
            Children.Remove(d);
        }
    }

    private static void Place(TextBlock tb, string text, double left, double top)
    {
        tb.Text = text;
        SetLeft(tb, left);
        SetTop(tb, top);
    }
}
