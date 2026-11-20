using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RedControlUtility;

/// <summary>
/// Propriedades anexadas de localização: &lt;TextBlock rcu:Loc.Key="Dashboard.Title"/&gt;.
/// Reaplicam o texto automaticamente quando o idioma muda.
/// </summary>
public static class Loc
{
    private static readonly List<WeakReference<DependencyObject>> Live = new();
    private static bool _hooked;

    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(string), typeof(Loc), new PropertyMetadata(null, OnKeyChanged));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.RegisterAttached("Header", typeof(string), typeof(Loc), new PropertyMetadata(null, OnKeyChanged));

    public static readonly DependencyProperty TipProperty =
        DependencyProperty.RegisterAttached("Tip", typeof(string), typeof(Loc), new PropertyMetadata(null, OnKeyChanged));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Loc), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject o) => (string?)o.GetValue(KeyProperty);
    public static void SetKey(DependencyObject o, string? v) => o.SetValue(KeyProperty, v);
    public static string? GetHeader(DependencyObject o) => (string?)o.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject o, string? v) => o.SetValue(HeaderProperty, v);
    public static string? GetTip(DependencyObject o) => (string?)o.GetValue(TipProperty);
    public static void SetTip(DependencyObject o, string? v) => o.SetValue(TipProperty, v);
    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? v) => o.SetValue(PlaceholderProperty, v);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!_hooked)
        {
            _hooked = true;
            App.Localization.LanguageChanged += ReapplyAll;
        }

        AddToLive(d);
        HookLifetime(d);
        Apply(d);
    }

    // Elementos que já tiveram o ciclo de vida inscrito (uma vez só por elemento, mesmo com Key+Header+Tip juntos).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, object> Hooked = new();

    private static bool InLive(DependencyObject d)
    {
        foreach (var wr in Live)
        {
            if (wr.TryGetTarget(out var t) && ReferenceEquals(t, d)) return true;
        }

        return false;
    }

    private static void AddToLive(DependencyObject d)
    {
        if (!InLive(d)) Live.Add(new WeakReference<DependencyObject>(d));
    }

    private static void RemoveFromLive(DependencyObject d)
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            if (!Live[i].TryGetTarget(out var t)) Live.RemoveAt(i);
            else if (ReferenceEquals(t, d)) Live.RemoveAt(i);
        }
    }

    private static void HookLifetime(DependencyObject d)
    {
        if (d is not FrameworkElement fe || Hooked.TryGetValue(d, out _)) return;
        Hooked.Add(d, new object());
        fe.Unloaded += (s, _) => RemoveFromLive((DependencyObject)s);
        fe.Loaded += (s, _) =>
        {
            var o = (DependencyObject)s;
            AddToLive(o);
            Apply(o); // o idioma pode ter mudado enquanto o elemento estava fora da árvore
        };
    }

    private static void ReapplyAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            if (Live[i].TryGetTarget(out var d)) Apply(d);
            else Live.RemoveAt(i);
        }
    }

    private static void Apply(DependencyObject d)
    {
        var loc = App.Localization;
        string? key = GetKey(d);
        if (key != null)
        {
            string text = loc.Get(key);
            switch (d)
            {
                case TextBlock tb: tb.Text = text; break;
                case ContentControl cc: cc.Content = text; break;
                case Microsoft.UI.Xaml.Documents.Run run: run.Text = text; break;
            }
        }

        string? header = GetHeader(d);
        if (header != null)
        {
            string text = loc.Get(header);
            switch (d)
            {
                case ToggleSwitch ts: ts.Header = text; break;
                case Slider sl: sl.Header = text; break;
                case NumberBox nb: nb.Header = text; break;
                case ComboBox cb: cb.Header = text; break;
                case TextBox tx: tx.Header = text; break;
                case Expander ex: ex.Header = text; break;
            }
        }

        string? tip = GetTip(d);
        if (tip != null && d is UIElement)
        {
            ToolTipService.SetToolTip(d, loc.Get(tip));
        }

        string? ph = GetPlaceholder(d);
        if (ph != null)
        {
            switch (d)
            {
                case ComboBox cb: cb.PlaceholderText = loc.Get(ph); break;
                case TextBox tx: tx.PlaceholderText = loc.Get(ph); break;
                case NumberBox nb: nb.PlaceholderText = loc.Get(ph); break;
            }
        }
    }
}
