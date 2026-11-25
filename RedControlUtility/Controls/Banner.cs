using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RedControlUtility.Services;
using Windows.UI;

namespace RedControlUtility.Controls;

public sealed class Banner : UserControl
{
    private readonly Border _root = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
    private readonly Border _stripe = new() { Width = 6 };
    private readonly FontIcon _icon = new() { FontSize = 20, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") };
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Button _close = new() { Content = new FontIcon { Glyph = "", FontSize = 12, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") }, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(8, 6, 8, 0) };
    private InfoBarSeverity _severity = InfoBarSeverity.Informational;
    private bool _open;
    private bool _closable = true;

    public Banner()
    {
        Visibility = Visibility.Collapsed;

        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 10, 12, 10) };
        texts.Children.Add(_title);
        texts.Children.Add(_message);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconHost = new Grid { Margin = new Thickness(14, 10, 0, 10), VerticalAlignment = VerticalAlignment.Top };
        iconHost.Children.Add(_icon);

        Grid.SetColumn(_stripe, 0);
        Grid.SetColumn(iconHost, 1);
        Grid.SetColumn(texts, 2);
        Grid.SetColumn(_close, 3);
        grid.Children.Add(_stripe);
        grid.Children.Add(iconHost);
        grid.Children.Add(texts);
        grid.Children.Add(_close);
        _root.Child = grid;
        Content = _root;

        _close.Click += (_, _) => IsOpen = false;
        Loaded += (_, _) =>
        {
            ThemeService.Changed -= Restyle;
            ThemeService.Changed += Restyle;
            Restyle();
        };
        Unloaded += (_, _) => ThemeService.Changed -= Restyle;
        Restyle();
    }

    public bool IsOpen
    {
        get => _open;
        set
        {
            _open = value;
            Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public bool IsClosable
    {
        get => _closable;
        set
        {
            _closable = value;
            _close.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public InfoBarSeverity Severity
    {
        get => _severity;
        set
        {
            _severity = value;
            Restyle();
        }
    }

    public string Title
    {
        get => _title.Text;
        set
        {
            _title.Text = value ?? string.Empty;
            _title.Visibility = string.IsNullOrEmpty(_title.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public string Message
    {
        get => _message.Text;
        set
        {
            _message.Text = value ?? string.Empty;
            _message.Visibility = string.IsNullOrEmpty(_message.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void Restyle()
    {
        var c = ThemeService.Colors;
        bool dark = ThemeService.IsDark;
        Color sev = _severity switch
        {
            InfoBarSeverity.Success => dark ? Color.FromArgb(255, 0x4A, 0xDE, 0x80) : Color.FromArgb(255, 0x15, 0x80, 0x3D),
            InfoBarSeverity.Warning => dark ? Color.FromArgb(255, 0xFB, 0xBF, 0x24) : Color.FromArgb(255, 0x9A, 0x5B, 0x00),
            InfoBarSeverity.Error => dark ? Color.FromArgb(255, 0xFF, 0x6B, 0x6B) : Color.FromArgb(255, 0xC6, 0x28, 0x28),
            _ => c.AccentText
        };
        _icon.Glyph = _severity switch
        {
            InfoBarSeverity.Success => "",
            InfoBarSeverity.Warning => "",
            InfoBarSeverity.Error => "",
            _ => ""
        };
        _root.Background = new SolidColorBrush(c.Surface);
        _root.BorderBrush = new SolidColorBrush(c.ControlBorder);
        _stripe.Background = new SolidColorBrush(sev);
        _stripe.CornerRadius = new CornerRadius(8, 0, 0, 8);
        _icon.Foreground = new SolidColorBrush(sev);
        _title.Foreground = new SolidColorBrush(c.Text);
        _message.Foreground = new SolidColorBrush(c.Text);
        _close.Foreground = new SolidColorBrush(c.Text);
        _close.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        _close.BorderThickness = new Thickness(0);
    }
}
