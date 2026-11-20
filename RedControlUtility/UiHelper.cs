using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RedControlUtility.Models;
using RedControlUtility.Services;
using RedControlUtility.Services.Adlx;

namespace RedControlUtility;

public static class UiHelper
{
    public static string L(string key) => App.Localization.Get(key);
    public static string L(string key, params object[] args) => App.Localization.Get(key, args);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<InfoBar, DispatcherQueueTimer> BarTimers = new();

    /// <summary>
    /// Mostra a mensagem sem "pular": a barra fica sobreposta (nao empurra a pagina), mensagens repetidas nao reabrem a animacao
    /// e as de sucesso/informacao somem sozinhas.
    /// </summary>
    public static void Show(InfoBar bar, InfoBarSeverity severity, string title, string message = "")
    {
        bool same = bar.IsOpen && bar.Severity == severity && bar.Title == title && bar.Message == message;
        if (!same)
        {
            bar.Severity = severity;
            bar.Title = title;
            bar.Message = message;
            if (!bar.IsOpen) bar.IsOpen = true;
        }

        try
        {
            if (!BarTimers.TryGetValue(bar, out var timer))
            {
                timer = bar.DispatcherQueue.CreateTimer();
                timer.IsRepeating = false;
                timer.Tick += (t, _) =>
                {
                    t.Stop();
                    bar.IsOpen = false;
                };
                BarTimers.Add(bar, timer);
            }

            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(severity is InfoBarSeverity.Error or InfoBarSeverity.Warning ? 14 : 5);
            timer.Start();
        }
        catch
        {
            // sem fila de UI (pagina fechando): a barra simplesmente nao fecha sozinha
        }
    }

    public static void ShowReport(InfoBar bar, ApplyReport report)
    {
        if (report.Empty)
        {
            Show(bar, InfoBarSeverity.Informational, L("Msg.NothingToApply"));
        }
        else if (report.AllOk)
        {
            Show(bar, InfoBarSeverity.Success, L("Msg.Applied"), report.Detail);
        }
        else if (report.AnyOk)
        {
            Show(bar, InfoBarSeverity.Warning, L("Msg.AppliedPartial"), report.Detail);
        }
        else
        {
            Show(bar, InfoBarSeverity.Error, L("Msg.ApplyFailed"), report.Detail);
        }
    }

    public static async Task<ContentDialogResult> ConfirmAsync(XamlRoot root, string title, string message, string primary, string close)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Close
        };
        return await dlg.ShowAsync();
    }

    /// <summary>Diálogo "manter alterações?" com contagem regressiva. Sem confirmação, retorna false (reverter).</summary>
    public static async Task<bool> ConfirmKeepAsync(XamlRoot root, int seconds)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        int left = seconds;
        void Update() => text.Text = L("Safe.Text", left);
        Update();
        var dlg = new ContentDialog
        {
            XamlRoot = root,
            Title = L("Safe.Title"),
            Content = text,
            PrimaryButtonText = L("Safe.Keep"),
            CloseButtonText = L("Safe.Revert"),
            DefaultButton = ContentDialogButton.Close
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            left--;
            if (left <= 0) { timer.Stop(); dlg.Hide(); }
            else Update();
        };
        timer.Start();
        var r = await dlg.ShowAsync();
        timer.Stop();
        return r == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Aplica ajuste fino com rede de segurança: grava um marcador, aplica, e (se ativado) pede confirmação em 15 s;
    /// sem confirmação, volta ao estado anterior. Se o PC travar, o marcador faz o app não reaplicar nada no próximo início.
    /// </summary>
    public static async Task<(ApplyReport Report, bool Kept)> ApplyTuningSafeAsync(XamlRoot root, TuningSettings settings)
    {
        var hw = App.Hardware;
        var before = await Task.Run(() => hw.ReadTuning());
        try { File.WriteAllText(AppPaths.PendingFlag, DateTime.Now.ToString("o")); } catch { }

        var report = await Task.Run(() => hw.ApplyTuning(settings));
        bool kept = true;
        if (report.AnyOk && App.Settings.Current.AutoRevertTuning)
        {
            kept = await ConfirmKeepAsync(root, 15);
            if (!kept)
            {
                var back = await Task.Run(() => hw.ApplyTuning(before));
                LogService.Info($"Ajuste revertido: {back.Summary}");
            }
        }

        try { File.Delete(AppPaths.PendingFlag); } catch { }
        return (report, kept);
    }

    public static ComboBoxItem Item(string text, object? tag = null) => new() { Content = text, Tag = tag };

    public static bool? TriToBool(int selectedIndex) => selectedIndex switch { 1 => true, 2 => false, _ => null };
    public static int BoolToTri(bool? v) => v switch { true => 1, false => 2, _ => 0 };
}

/// <summary>Agrupa chamadas rápidas (ex.: slider arrastado) numa única execução após <c>ms</c> de calmaria.</summary>
public sealed class Debouncer
{
    private readonly DispatcherQueueTimer _timer;

    public Debouncer(DispatcherQueue queue, int milliseconds, Action action)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => action();
    }

    public void Trigger()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel() => _timer.Stop();
}
