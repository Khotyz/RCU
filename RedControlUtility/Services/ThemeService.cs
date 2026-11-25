using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RedControlUtility.Models;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace RedControlUtility.Services;

public static class ThemeService
{
    private static readonly UISettings Ui = new();
    private static MainWindow? _window;
    private static bool _hooked;

    public static ElementTheme Element { get; private set; } = ElementTheme.Dark;
    public static bool IsDark { get; private set; } = true;

    public static event Action? Changed;

    public sealed record ThemeColors(Color Window, Color Panel, Color Surface, Color Border, Color ControlBorder, Color Text, Color Muted, Color Accent, Color AccentText);

    private sealed record Def(
        bool Dark, bool Custom, string Window, string Panel, string Surface, string Border, string ControlBorder,
        string Text, string Muted, string Accent, string AccentText, string[]? Ramp = null);

    private static readonly string[] RedRamp = { "#E5252A", "#FF5A5F", "#FF7A7E", "#FF9A9D", "#C21B20", "#9E1418", "#7A0F12" };

    private static readonly Dictionary<AppTheme, Def> Defs = new()
    {
        [AppTheme.Dark] = new(true, false, "#202020", "#2B2B2B", "#2B2B2B", "#3A3A3A", "#7A7A7A", "#FFFFFF", "#B4B4B4", "#E5252A", "#FF5A5F", RedRamp),
        [AppTheme.Light] = new(false, false, "#F3F3F3", "#FFFFFF", "#FFFFFF", "#D0D0D0", "#7A7A7A", "#1A1A1A", "#4F4F4F", "#C21B20", "#C21B20", RedRamp),
        [AppTheme.Dracula] = new(true, true, "#282A36", "#343746", "#383B4D", "#44475A", "#6272A4", "#F8F8F2", "#C8C9D3", "#BD93F9", "#BD93F9"),
        [AppTheme.CyberGreen] = new(true, true, "#0D0E10", "#17191C", "#1D2024", "#26292E", "#4B525C", "#F3F4F6", "#9CA3AF", "#76B900", "#76B900"),
        [AppTheme.CrimsonCarbon] = new(true, true, "#111113", "#1C1C1F", "#232327", "#2E2E33", "#55555E", "#FFFFFF", "#A1A1AA", "#ED1C24", "#F43F5E"),
        [AppTheme.ArcCobalt] = new(true, true, "#0B101B", "#131B2E", "#1A2540", "#1E293B", "#41516B", "#F8FAFC", "#94A3B8", "#38BDF8", "#38BDF8")
    };

    public static ThemeColors Colors { get; private set; } = Build(Defs[AppTheme.Dark]);

    private static readonly string[] AccentKeys =
    {
        "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3"
    };

    public static SolidColorBrush SurfaceBrush() => new(Colors.Surface);
    public static SolidColorBrush WindowBrush() => new(Colors.Window);
    public static SolidColorBrush PanelBrush() => new(Colors.Panel);
    public static SolidColorBrush BorderBrush() => new(Colors.ControlBorder);
    public static SolidColorBrush TextBrush() => new(Colors.Text);
    public static SolidColorBrush MutedBrush() => new(Colors.Muted);
    public static SolidColorBrush AccentBrush() => new(Colors.Accent);

    public static void Apply(MainWindow window)
    {
        _window = window;
        if (!_hooked)
        {
            Ui.ColorValuesChanged += (_, _) =>
            {
                if (App.Settings.Current.Theme != (int)AppTheme.System) return;
                _window?.DispatcherQueue.TryEnqueue(Refresh);
            };
            _hooked = true;
        }

        Refresh();
    }

    public static void Refresh()
    {
        var window = _window;
        if (window == null) return;
        try
        {
            var mode = (AppTheme)App.Settings.Current.Theme;
            if (mode == AppTheme.System) mode = SystemIsDark() ? AppTheme.Dark : AppTheme.Light;
            if (!Defs.TryGetValue(mode, out var def)) def = Defs[AppTheme.Dark];

            IsDark = def.Dark;
            Element = def.Dark ? ElementTheme.Dark : ElementTheme.Light;
            Colors = Build(def);
            ApplyResources(def);

            var root = window.RootElement;
            root.RequestedTheme = def.Dark ? ElementTheme.Light : ElementTheme.Dark;
            root.RequestedTheme = Element;

            if (def.Custom)
            {
                window.SystemBackdrop = null;
                window.RootPanel.Background = new SolidColorBrush(Colors.Window);
            }
            else
            {
                window.RootPanel.Background = null;
                window.SystemBackdrop ??= new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
            }

            ApplyTitleBar(window);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            LogService.Error($"Theme: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message} {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
        }
    }

    public static IAsyncOperation<ContentDialogResult> ShowAsync(ContentDialog dlg)
    {
        dlg.RequestedTheme = Element;
        dlg.Background = SurfaceBrush();
        dlg.Foreground = TextBrush();
        dlg.BorderBrush = BorderBrush();
        dlg.BorderThickness = new Thickness(1);
        dlg.Opened += (s, _) => PaintDialog(s);
        return dlg.ShowAsync();
    }

    private static void PaintDialog(DependencyObject root)
    {
        try
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                int n = VisualTreeHelper.GetChildrenCount(cur);
                for (int i = 0; i < n; i++)
                {
                    var child = VisualTreeHelper.GetChild(cur, i);
                    if (child is FrameworkElement fe)
                    {
                        if (fe.Name == "BackgroundElement" && fe is Border be)
                        {
                            be.Background = SurfaceBrush();
                            be.BorderBrush = BorderBrush();
                        }
                        else if (fe.Name == "CommandSpace" && fe is Panel cp) cp.Background = WindowBrush();
                        else if (fe.Name == "CommandSpace" && fe is Border cb) cb.Background = WindowBrush();
                        else if (fe.Name == "DialogSpace" && fe is Panel dp) dp.Background = SurfaceBrush();
                        else if (fe.Name == "ContentScrollViewer" && fe is Control sc) sc.Background = SurfaceBrush();
                        else if (fe.Name == "Content" && fe is Control cc) cc.Foreground = TextBrush();
                        else if (fe.Name == "TitleArea" && fe is Panel tp) tp.Background = SurfaceBrush();
                    }

                    stack.Push(child);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"Theme dialog: {ex.Message}");
        }
    }

    private static bool SystemIsDark()
    {
        var c = Ui.GetColorValue(UIColorType.Foreground);
        return c.R + c.G + c.B > 384;
    }

    private static ThemeColors Build(Def d) => new(
        Hex(d.Window), Hex(d.Panel), Hex(d.Surface), Hex(d.Border), Hex(d.ControlBorder), Hex(d.Text), Hex(d.Muted), Hex(d.Accent), Hex(d.AccentText));

    private static Color Blend(Color a, Color b, double f) => Color.FromArgb(
        255,
        (byte)Math.Round(a.R + (b.R - a.R) * f),
        (byte)Math.Round(a.G + (b.G - a.G) * f),
        (byte)Math.Round(a.B + (b.B - a.B) * f));

    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static bool Bright(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 > 0.55;

    private static ResourceDictionary? _overlay;

    private static void Set(ResourceDictionary dict, Color color, params string[] keys)
    {
        foreach (var k in keys) dict[k] = new SolidColorBrush(color);
    }

    private static void FillBase(ResourceDictionary dict, Def d)
    {
        Color w = Hex(d.Window), p = Hex(d.Panel), bd = Hex(d.Border), cb = Hex(d.ControlBorder), t = Hex(d.Text), m = Hex(d.Muted);
        Color black = Hex("#000000");
        Color raised = Blend(p, t, 0.06), hover = Blend(p, t, 0.10), layer = Blend(w, p, 0.6), mid = Blend(w, p, 0.5), deep = Blend(w, black, 0.25);

        Set(dict, w, "SolidBackgroundFillColorBaseBrush", "LayerOnMicaBaseAltFillColorTertiaryBrush", "ControlFillColorInputActiveBrush", "LayerOnMicaBaseAltFillColorTertiaryBrush");
        Set(dict, deep, "SolidBackgroundFillColorBaseAltBrush", "NavigationViewDefaultPaneBackground", "NavigationViewExpandedPaneBackground", "NavigationViewTopPaneBackground");
        Set(dict, mid, "SolidBackgroundFillColorSecondaryBrush", "ControlAltFillColorSecondaryBrush");
        Set(dict, p, "SolidBackgroundFillColorTertiaryBrush", "LayerOnMicaBaseAltFillColorSecondaryBrush", "LayerOnAcrylicFillColorDefaultBrush", "CardBackgroundFillColorDefaultBrush", "ControlAltFillColorTertiaryBrush");
        Set(dict, raised, "SolidBackgroundFillColorQuarternaryBrush", "ControlFillColorDefaultBrush", "ControlSolidFillColorDefaultBrush", "ControlAltFillColorQuarternaryBrush", "SubtleFillColorTertiaryBrush");
        Set(dict, layer, "LayerFillColorDefaultBrush", "LayerFillColorAltBrush", "LayerOnMicaBaseAltFillColorDefaultBrush", "CardBackgroundFillColorSecondaryBrush", "ControlFillColorTertiaryBrush", "ControlFillColorDisabledBrush", "ControlAltFillColorDisabledBrush");
        Set(dict, hover, "ControlFillColorSecondaryBrush", "SubtleFillColorSecondaryBrush");
        Set(dict, Hex(d.Surface), "AcrylicBackgroundFillColorDefaultBrush", "AcrylicBackgroundFillColorBaseBrush", "AcrylicInAppFillColorDefaultBrush", "AcrylicInAppFillColorBaseBrush");
        Set(dict, bd, "CardStrokeColorDefaultBrush", "CardStrokeColorDefaultSolidBrush", "DividerStrokeColorDefaultBrush");
        Set(dict, cb, "ControlStrokeColorDefaultBrush", "ControlStrokeColorSecondaryBrush", "SurfaceStrokeColorDefaultBrush", "SurfaceStrokeColorFlyoutBrush");
        Set(dict, m, "ControlStrongStrokeColorDefaultBrush", "ControlStrongFillColorDefaultBrush");
        Set(dict, t, "TextFillColorPrimaryBrush", "SystemControlForegroundBaseHighBrush");
        Set(dict, m, "TextFillColorSecondaryBrush", "SystemControlForegroundBaseMediumBrush");
        Set(dict, Blend(m, w, 0.25), "TextFillColorTertiaryBrush");
        Set(dict, Blend(m, w, 0.5), "TextFillColorDisabledBrush");

        Color acc = Hex(d.Accent), accText = Hex(d.AccentText);
        Color onAcc = Bright(acc) ? Hex("#0D0E10") : Hex("#FFFFFF");
        Set(dict, acc, "AccentFillColorDefaultBrush");
        Set(dict, Blend(acc, w, 0.12), "AccentFillColorSecondaryBrush");
        Set(dict, Blend(acc, w, 0.24), "AccentFillColorTertiaryBrush");
        Set(dict, Blend(m, w, 0.5), "AccentFillColorDisabledBrush");
        Set(dict, onAcc, "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush", "TextOnAccentFillColorSelectedTextBrush");
        Set(dict, accText, "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush", "SystemFillColorAttentionBrush");
        Set(dict, Blend(acc, w, 0.15), "AccentFillColorSelectedTextBackgroundBrush");

        Set(dict, bd, "ListViewItemBackgroundSelected");
        Set(dict, hover, "ListViewItemBackgroundPointerOver", "ListViewItemBackgroundSelectedPointerOver", "GridViewItemBackgroundPointerOver", "GridViewItemBackgroundSelectedPointerOver");
        Set(dict, Blend(p, t, 0.16), "ListViewItemBackgroundPressed", "ListViewItemBackgroundSelectedPressed", "GridViewItemBackgroundPressed", "GridViewItemBackgroundSelectedPressed");
        Set(dict, bd, "GridViewItemBackgroundSelected");
    }

    private static void FillControls(ResourceDictionary dict, Def d)
    {
        Color w = Hex(d.Window), p = Hex(d.Panel), cb = Hex(d.ControlBorder), t = Hex(d.Text), m = Hex(d.Muted);
        Color acc = Hex(d.Accent), accText = Hex(d.AccentText);
        Color onAcc = Bright(acc) ? Hex("#0D0E10") : Hex("#FFFFFF");
        Color raised = Blend(p, t, 0.06), hover = Blend(p, t, 0.12), pressed = Blend(p, t, 0.18), dis = Blend(m, w, 0.5), disBg = Blend(w, p, 0.5);
        Color strongBorder = Blend(cb, t, 0.25);

        Set(dict, w, "TextControlBackground", "TextControlBackgroundFocused");
        Set(dict, Blend(w, t, 0.05), "TextControlBackgroundPointerOver");
        Set(dict, disBg, "TextControlBackgroundDisabled");
        Set(dict, t, "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused");
        Set(dict, dis, "TextControlForegroundDisabled", "TextControlPlaceholderForegroundDisabled");
        Set(dict, cb, "TextControlBorderBrush", "TextControlBorderBrushDisabled");
        Set(dict, strongBorder, "TextControlBorderBrushPointerOver");
        Set(dict, acc, "TextControlBorderBrushFocused");
        Set(dict, m, "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused");
        Set(dict, Blend(acc, w, 0.45), "TextControlSelectionHighlightColor");
        Set(dict, p, "TextControlButtonBackground");
        Set(dict, hover, "TextControlButtonBackgroundPointerOver");
        Set(dict, pressed, "TextControlButtonBackgroundPressed");
        Set(dict, cb, "TextControlButtonBorderBrush", "TextControlButtonBorderBrushPointerOver", "TextControlButtonBorderBrushPressed");
        Set(dict, t, "TextControlButtonForeground", "TextControlButtonForegroundPointerOver", "TextControlButtonForegroundPressed");

        Set(dict, raised, "ComboBoxBackground");
        Set(dict, hover, "ComboBoxBackgroundPointerOver");
        Set(dict, pressed, "ComboBoxBackgroundPressed");
        Set(dict, raised, "ComboBoxBackgroundFocused");
        Set(dict, disBg, "ComboBoxBackgroundDisabled");
        Set(dict, t, "ComboBoxForeground", "ComboBoxForegroundPointerOver", "ComboBoxForegroundPressed", "ComboBoxForegroundFocused", "ComboBoxDropDownGlyphForeground", "ComboBoxFocusedDropDownGlyphForeground");
        Set(dict, dis, "ComboBoxForegroundDisabled");
        Set(dict, m, "ComboBoxPlaceHolderForeground", "ComboBoxPlaceHolderForegroundFocusedPressed");
        Set(dict, cb, "ComboBoxBorderBrush", "ComboBoxBorderBrushPressed", "ComboBoxBorderBrushDisabled");
        Set(dict, strongBorder, "ComboBoxBorderBrushPointerOver");
        Set(dict, acc, "ComboBoxBorderBrushFocused");

        Set(dict, raised, "ButtonBackground");
        Set(dict, hover, "ButtonBackgroundPointerOver");
        Set(dict, pressed, "ButtonBackgroundPressed");
        Set(dict, disBg, "ButtonBackgroundDisabled");
        Set(dict, t, "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed");
        Set(dict, dis, "ButtonForegroundDisabled");
        Set(dict, cb, "ButtonBorderBrush", "ButtonBorderBrushPressed", "ButtonBorderBrushDisabled");
        Set(dict, strongBorder, "ButtonBorderBrushPointerOver");

        Set(dict, acc, "AccentButtonBackground");
        Set(dict, Blend(acc, w, 0.12), "AccentButtonBackgroundPointerOver");
        Set(dict, Blend(acc, w, 0.24), "AccentButtonBackgroundPressed");
        Set(dict, disBg, "AccentButtonBackgroundDisabled");
        Set(dict, onAcc, "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed");
        Set(dict, dis, "AccentButtonForegroundDisabled");
        Set(dict, acc, "AccentButtonBorderBrush", "AccentButtonBorderBrushPointerOver", "AccentButtonBorderBrushPressed");

        Set(dict, p, "CheckBoxBackground", "RadioButtonBackground");
        Set(dict, cb, "CheckBoxCheckBackgroundStrokeUnchecked", "CheckBoxCheckBackgroundStrokeUncheckedPointerOver", "CheckBoxCheckBackgroundStrokeUncheckedPressed");
        Set(dict, acc, "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPointerOver");
        Set(dict, onAcc, "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed");
        Set(dict, t, "CheckBoxForegroundUnchecked", "CheckBoxForegroundChecked", "CheckBoxForegroundUncheckedPointerOver", "CheckBoxForegroundCheckedPointerOver");
        Set(dict, acc, "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver");
        Set(dict, onAcc, "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed");
        Set(dict, cb, "ToggleSwitchStrokeOff", "ToggleSwitchStrokeOffPointerOver");
        Set(dict, Blend(m, t, 0.15), "ToggleSwitchKnobFillOff", "ToggleSwitchKnobFillOffPointerOver", "ToggleSwitchKnobFillOffPressed");
        Set(dict, acc, "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed", "ProgressBarForeground");
        Set(dict, Blend(w, t, 0.25), "SliderTrackFill", "SliderTrackFillPointerOver", "ProgressBarBackground");
        Set(dict, acc, "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed");
    }

    private static void ApplyChrome(ResourceDictionary dict, Def d)
    {
        Color surface = Hex(d.Surface), border = Hex(d.ControlBorder), text = Hex(d.Text), w = Hex(d.Window);
        Color hover = Blend(surface, text, 0.10), selected = Blend(surface, text, 0.16);

        Set(dict, surface,
            "ComboBoxDropDownBackground", "MenuFlyoutPresenterBackground", "FlyoutPresenterBackground", "ToolTipBackground",
            "ContentDialogBackground", "InfoBarInformationalSeverityBackgroundBrush", "InfoBarSuccessSeverityBackgroundBrush",
            "InfoBarWarningSeverityBackgroundBrush", "InfoBarErrorSeverityBackgroundBrush", "AutoSuggestBoxSuggestionsListBackground",
            "SystemFillColorAttentionBackgroundBrush", "SystemFillColorSuccessBackgroundBrush", "SystemFillColorCautionBackgroundBrush",
            "SystemFillColorCriticalBackgroundBrush", "SystemFillColorNeutralBackgroundBrush", "SystemFillColorSolidAttentionBackgroundBrush",
            "SystemFillColorSolidNeutralBackgroundBrush", "DatePickerFlyoutPresenterBackground", "TimePickerFlyoutPresenterBackground");
        Set(dict, w, "ContentDialogCommandSpaceBackground");
        Set(dict, border, "ComboBoxDropDownBorderBrush", "MenuFlyoutPresenterBorderBrush", "FlyoutBorderThemeBrush", "ToolTipBorderBrush",
            "ContentDialogBorderBrush", "InfoBarBorderBrush", "ContentDialogSeparatorBorderBrush");
        Set(dict, hover, "ComboBoxItemBackgroundPointerOver", "ComboBoxItemBackgroundSelectedPointerOver", "MenuFlyoutItemBackgroundPointerOver");
        Set(dict, selected, "ComboBoxItemBackgroundPressed", "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPressed", "MenuFlyoutItemBackgroundPressed");
        Set(dict, text,
            "ComboBoxItemForeground", "ComboBoxItemForegroundPointerOver", "ComboBoxItemForegroundPressed", "ComboBoxItemForegroundSelected",
            "ComboBoxItemForegroundSelectedPointerOver", "ComboBoxItemForegroundSelectedPressed", "MenuFlyoutItemForeground",
            "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed", "ToolTipForeground", "ContentDialogForeground",
            "InfoBarTitleForeground", "InfoBarMessageForeground");
    }

    private static void ApplyResources(Def d)
    {
        var res = Application.Current.Resources;
        if (_overlay != null) res.MergedDictionaries.Remove(_overlay);
        var overlay = new ResourceDictionary();
        foreach (var name in new[] { "Default", "Dark", "Light" })
        {
            var dict = new ResourceDictionary();
            if (d.Custom)
            {
                FillBase(dict, d);
                FillControls(dict, d);
            }

            ApplyChrome(dict, d);
            overlay.ThemeDictionaries[name] = dict;
        }

        res.MergedDictionaries.Add(overlay);
        _overlay = overlay;

        Color acc = Hex(d.Accent), accText = Hex(d.AccentText), w = Hex(d.Window), t = Hex(d.Text), bd = Hex(d.Border), m = Hex(d.Muted);
        string[] ramp = d.Ramp ?? new[]
        {
            d.Accent,
            Hex2(Blend(acc, Hex("#FFFFFF"), 0.20)), Hex2(Blend(acc, Hex("#FFFFFF"), 0.40)), Hex2(Blend(acc, Hex("#FFFFFF"), 0.60)),
            Hex2(Blend(acc, Hex("#000000"), 0.15)), Hex2(Blend(acc, Hex("#000000"), 0.30)), Hex2(Blend(acc, Hex("#000000"), 0.45))
        };
        for (int i = 0; i < AccentKeys.Length; i++) res[AccentKeys[i]] = Hex(ramp[i]);

        res["AppAccentText"] = new SolidColorBrush(accText);
        res["CurveLineBrush"] = new SolidColorBrush(accText);
        res["CurvePointStrokeBrush"] = new SolidColorBrush(acc);
        res["CurveMarqueeBrush"] = new SolidColorBrush(Alpha(accText, 0x28));
        res["CurveMarqueeStrokeBrush"] = new SolidColorBrush(Alpha(accText, 0xDC));
        res["CurveGridBrush"] = new SolidColorBrush(Alpha(d.Custom ? Hex(d.ControlBorder) : t, d.Custom ? (byte)0x80 : (byte)0x26));
        res["CurveGhostBrush"] = new SolidColorBrush(Alpha(m, 0x96));
    }

    private static void ApplyTitleBar(MainWindow window)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var tb = window.AppWindow.TitleBar;
        Color fg = Colors.Text;
        Color hover = Alpha(Colors.Text, 0x1E);
        Color pressed = Alpha(Colors.Text, 0x32);
        tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonForegroundColor = fg;
        tb.ButtonInactiveForegroundColor = Colors.Muted;
        tb.ButtonHoverBackgroundColor = hover;
        tb.ButtonHoverForegroundColor = fg;
        tb.ButtonPressedBackgroundColor = pressed;
        tb.ButtonPressedForegroundColor = fg;
    }

    private static string Hex2(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255;
        int o = 0;
        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex.Substring(0, 2), 16);
            o = 2;
        }

        return Color.FromArgb(a, Convert.ToByte(hex.Substring(o, 2), 16), Convert.ToByte(hex.Substring(o + 2, 2), 16), Convert.ToByte(hex.Substring(o + 4, 2), 16));
    }
}
