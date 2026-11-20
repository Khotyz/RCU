using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RedControlUtility.Models;
using RedControlUtility.Services;
using RedControlUtility.Services.Adlx;
using static RedControlUtility.UiHelper;

namespace RedControlUtility.Views;

public sealed partial class AppsPage : Page
{
    private bool _loading = true;
    private AppProfile? _app;
    private Debouncer? _saveDebounce;

    private static List<AppProfile> Apps => App.CurrentProfile.Apps;

    public AppsPage()
    {
        InitializeComponent();
        foreach (var ed in new[] { FrtcEditor, ChillMinEditor, ChillMaxEditor, RisEditor, RsrEditor, BoostEditor, PowerEditor })
            ed.ValueChanged += Changed;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _saveDebounce = new Debouncer(DispatcherQueue, 400, SaveNow);
        App.Localization.LanguageChanged += Rebuild;
        App.Engine.ActiveChanged += OnActiveChanged;
        Rebuild();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _saveDebounce?.Cancel();
        SaveNow();
        App.Localization.LanguageChanged -= Rebuild;
        App.Engine.ActiveChanged -= OnActiveChanged;
    }

    private void OnActiveChanged(AppProfile? p) => DispatcherQueue.TryEnqueue(() =>
    {
        UpdateActiveText();
        _loading = true;
        try { RefreshList(_app); } finally { _loading = false; }
    });

    private void UpdateActiveText()
    {
        var a = App.Engine.Active;
        ActiveText.Text = !App.Settings.Current.PerAppProfilesEnabled ? L("Apps.EngineOff")
            : a == null ? L("Apps.NoneActive")
            : L("Apps.ActiveNow", a.Name);
    }

    private void Rebuild()
    {
        _loading = true;
        try
        {
            // Relê o que o driver oferece agora: as opções estavam sendo liberadas com o resultado
            // de uma leitura antiga (feita antes do jogo abrir), e várias ficavam cinzas.
            App.Hardware.ReadGraphics();
            BuildCombos();
            RefreshList(_app);
            UpdateActiveText();
        }
        finally
        {
            _loading = false;
        }

        if (_app != null) Fill(_app);
    }

    private static void FillTri(ComboBox cb)
    {
        cb.Items.Clear();
        cb.Items.Add(Item(L("Common.Global")));
        cb.Items.Add(Item(L("Common.On")));
        cb.Items.Add(Item(L("Common.Off")));
    }

    private void BuildCombos()
    {
        foreach (var cb in new[] { AntiLagCombo, AfmfCombo, FsrCombo, EnhSyncCombo, ChillCombo, FrtcCombo, RisCombo, RsrCombo, BoostCombo, MlaaCombo, AfCombo, TripleBufferCombo, SurfaceFormatCombo })
            FillTri(cb);

        BuildAdvancedCombos();

        VSyncCombo.Items.Clear();
        VSyncCombo.Items.Add(Item(L("Common.Global"), null));
        VSyncCombo.Items.Add(Item(L("VSync.AlwaysOff"), (int)AdlxVSyncMode.AlwaysOff));
        VSyncCombo.Items.Add(Item(L("VSync.OffUnlessApp"), (int)AdlxVSyncMode.OffUnlessAppSpecifies));
        VSyncCombo.Items.Add(Item(L("VSync.OnUnlessApp"), (int)AdlxVSyncMode.OnUnlessAppSpecifies));
        VSyncCombo.Items.Add(Item(L("VSync.AlwaysOn"), (int)AdlxVSyncMode.AlwaysOn));

        PowerCombo.Items.Clear();
        PowerCombo.Items.Add(Item(L("Common.Global")));
        PowerCombo.Items.Add(Item(L("Apps.PowerCustom")));

        ConfigureEditors();

        PresetCombo.Items.Clear();
        PresetCombo.Items.Add(Item(L("Common.Global"), null));
        foreach (var p in App.Hardware.Caps.Tuning.Presets) PresetCombo.Items.Add(Item(p, p));
    }

    private void BuildAdvancedCombos()
    {
        void Tags(ComboBox cb, params (string Text, int? Tag)[] items)
        {
            cb.Items.Clear();
            foreach (var (text, tag) in items) cb.Items.Add(Item(text, tag));
        }

        string g = L("Common.Global");
        Tags(AaModeCombo, (g, null), (L("Aa.UseApp"), 0), (L("Aa.Enhance"), 1), (L("Aa.Override"), 2));
        Tags(AaMethodCombo, (g, null), (L("Aa.Multi"), 0), (L("Aa.Adaptive"), 1), (L("Aa.Super"), 2));
        Tags(AaLevelCombo, (g, null), ("2x", 2), ("4x", 4), ("8x", 8));
        Tags(AfLevelCombo, ("2x", 2), ("4x", 4), ("8x", 8), ("16x", 16));
        Tags(TessCombo, (g, null), (L("Tess.Amd"), 0), (L("Tess.UseApp"), 1), (L("Tess.Override"), 2));
        Tags(TessLevelCombo, (L("Tess.Off"), 1), ("2x", 2), ("4x", 4), ("6x", 6), ("8x", 8), ("16x", 16), ("32x", 32), ("64x", 64));
    }

    private static int? SelTag(ComboBox cb) => cb.SelectedItem is ComboBoxItem { Tag: int v } ? v : null;

    private void RefreshList(AppProfile? select)
    {
        AppsList.Items.Clear();
        foreach (var a in Apps)
        {
            AppsList.Items.Add(new ListViewItem { Content = BuildListEntry(a), Tag = a });
        }

        EmptyText.Visibility = Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.IsEnabled = Apps.Count > 0;
        int idx = select == null ? -1 : Apps.IndexOf(select);
        AppsList.SelectedIndex = idx;
        if (idx < 0)
        {
            _app = null;
            ShowEditor(false);
        }
    }

    /// <summary>
    /// O editor fica sempre no layout (só muda de habilitado para desabilitado). Assim a tela já nasce
    /// "expandida" e nenhuma opção faz o cartão crescer ou encolher ao ser ligada.
    /// </summary>
    private void ShowEditor(bool hasApp)
    {
        EditorEmpty.Visibility = hasApp ? Visibility.Collapsed : Visibility.Visible;
        EditorBody.IsEnabled = hasApp;
        EditorBody.Opacity = hasApp ? 1 : 0.45;
    }

    private void AppsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (AppsList.SelectedItem is ListViewItem { Tag: AppProfile a })
        {
            SaveNow();
            _app = a;
            Fill(a);
        }
        else
        {
            _app = null;
            ShowEditor(false);
        }
    }

    // Valores sugeridos quando a pessoa só liga a opção (o mais comum entre os jogadores).
    private const int DefFrtc = 60, DefRis = 80, DefRsr = 80, DefBoost = 50, DefChillMin = 70, DefChillMax = 144;

    private void ConfigureEditors()
    {
        var c = App.Hardware.Caps.Graphics;
        var t = App.Hardware.Caps.Tuning;
        FrtcEditor.Format = null;
        FrtcEditor.Configure(c.FrtcRange.IsValid ? c.FrtcRange.Min : 30, c.FrtcRange.IsValid ? c.FrtcRange.Max : 300, 1, "fps", new[] { 30, 60, 75, 90, 120, 144, 165, 240 });
        int cMin = c.ChillRange.IsValid ? c.ChillRange.Min : 30, cMax = c.ChillRange.IsValid ? c.ChillRange.Max : 300;
        ChillMinEditor.Configure(cMin, cMax, 1, "fps", new[] { 40, 60, 70 });
        ChillMaxEditor.Configure(cMin, cMax, 1, "fps", new[] { 60, 90, 120, 144 });
        RisEditor.Configure(0, 100, 1, "%", new[] { 50, 80, 100 });
        RsrEditor.Configure(0, 100, 1, "%", new[] { 50, 80, 100 });
        BoostEditor.Configure(c.BoostRange.IsValid ? c.BoostRange.Min : 30, c.BoostRange.IsValid ? c.BoostRange.Max : 100, 1, "%", new[] { 50, 70, 85 });
        PowerEditor.Format = v => $"{v:+0;-0;0}%";
        PowerEditor.Configure(t.PowerRange.IsValid ? t.PowerRange.Min : -50, t.PowerRange.IsValid ? t.PowerRange.Max : 50, 1, "%", new[] { -10, 0, 10 });
    }

    private static int Def(int? v, int fallback) => v ?? fallback;

    /// <summary>
    /// Mostra o controle de valor só quando a opção está ligada. O espaço fica reservado (opacidade, não Collapsed),
    /// senão cada troca de Global/Ligado/Desligado redimensiona o cartão inteiro e a tela parece "pulando".
    /// </summary>
    private void UpdateEditorVisibility()
    {
        static void Reserve(FrameworkElement el, ComboBox cb)
        {
            bool on = cb.SelectedIndex == 1;
            el.Visibility = Visibility.Visible;
            el.Opacity = on ? 1 : 0;
            el.IsHitTestVisible = on;
            if (el is Control c) c.IsTabStop = on;
        }

        Reserve(ChillPanel, ChillCombo);
        Reserve(RisEditor, RisCombo);
        Reserve(RsrEditor, RsrCombo);
        Reserve(BoostEditor, BoostCombo);
        Reserve(PowerEditor, PowerCombo);

        void ReserveIf(FrameworkElement el, bool on)
        {
            el.Opacity = on ? 1 : 0;
            el.IsHitTestVisible = on;
        }

        ReserveIf(AfLevelCombo, AfCombo.SelectedIndex == 1);
        ReserveIf(TessLevelCombo, SelTag(TessCombo) == 2);
    }

    private static void SelectByTag(ComboBox cb, object? tag)
    {
        cb.SelectedIndex = 0;
        for (int i = 0; i < cb.Items.Count; i++)
        {
            if (cb.Items[i] is ComboBoxItem it && Equals(it.Tag, tag)) { cb.SelectedIndex = i; return; }
        }
    }

    private void Fill(AppProfile a)
    {
        _loading = true;
        try
        {
            ShowEditor(true);
            NameBox.Text = a.Name;
            PathText.Text = a.ExecutablePath;
            SetIcon(AppIconImage, a, 40);
            EnabledToggle.IsOn = a.Enabled;
            KeepCheck.IsChecked = a.KeepAfterExit;
            var g = a.Graphics;
            AntiLagCombo.SelectedIndex = BoolToTri(g.AntiLag);
            AfmfCombo.SelectedIndex = BoolToTri(g.Afmf);
            FsrCombo.SelectedIndex = BoolToTri(g.Fsr);
            EnhSyncCombo.SelectedIndex = BoolToTri(g.EnhancedSync);
            ChillCombo.SelectedIndex = BoolToTri(g.Chill);
            ChillMinEditor.SetValue(Def(g.ChillMinFps, DefChillMin));
            ChillMaxEditor.SetValue(Def(g.ChillMaxFps, DefChillMax));
            FrtcCombo.SelectedIndex = BoolToTri(g.Frtc);
            FrtcEditor.SetValue(Def(g.FrtcFps, DefFrtc));
            RisCombo.SelectedIndex = BoolToTri(g.ImageSharpening);
            RisEditor.SetValue(Def(g.ImageSharpeningLevel, DefRis));
            RsrCombo.SelectedIndex = BoolToTri(g.Rsr);
            RsrEditor.SetValue(Def(g.RsrSharpness, DefRsr));
            BoostCombo.SelectedIndex = BoolToTri(g.Boost);
            BoostEditor.SetValue(Def(g.BoostResolution, DefBoost));
            SelectByTag(VSyncCombo, g.VSyncMode);
            SelectByTag(AaModeCombo, g.AaMode);
            SelectByTag(AaMethodCombo, g.AaMethod);
            SelectByTag(AaLevelCombo, g.AaLevel);
            MlaaCombo.SelectedIndex = BoolToTri(g.Mlaa);
            AfCombo.SelectedIndex = BoolToTri(g.AnisotropicFiltering);
            SelectByTag(AfLevelCombo, g.AnisotropicLevel ?? 16);
            SelectByTag(TessCombo, g.TessellationMode);
            SelectByTag(TessLevelCombo, g.TessellationLevel ?? 64);
            TripleBufferCombo.SelectedIndex = BoolToTri(g.TripleBuffer);
            SurfaceFormatCombo.SelectedIndex = BoolToTri(g.SurfaceFormatOpt);
            PowerCombo.SelectedIndex = a.PowerLimitPercent.HasValue ? 1 : 0;
            PowerEditor.SetValue(a.PowerLimitPercent ?? 0);
            SelectByTag(PresetCombo, a.TuningPreset);

            var c = App.Hardware.Caps.Graphics;
            AntiLagCombo.IsEnabled = c.AntiLag; AfmfCombo.IsEnabled = c.Afmf;

            // FSR: RDNA 2 em diante. Por ADLX quando o driver expõe; senão pelo perfil do AMD Software, se ele existir (opcional).
            bool fsrGpu = App.Hardware.CurrentGpu.Architecture.SupportsFsr4();
            bool fsrDb = !c.Fsr && fsrGpu && Services.Adl.DriverFsrService.IsSupported();
            FsrCombo.IsEnabled = fsrGpu && (c.Fsr || fsrDb);
            FsrNote.Visibility = FsrCombo.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
            FsrNote.Text = !fsrGpu ? L("Apps.FsrOldGpu", App.Hardware.CurrentGpu.ArchitectureText)
                : fsrDb ? L("Apps.FsrViaDriver")
                : L("Apps.FsrDriverOnly");

            AdvancedExpander.Visibility = App.Settings.Current.AdvancedMode ? Visibility.Visible : Visibility.Collapsed;
            AaModeCombo.IsEnabled = AaMethodCombo.IsEnabled = AaLevelCombo.IsEnabled = c.AntiAliasing;
            MlaaCombo.IsEnabled = c.Mlaa;
            AfCombo.IsEnabled = AfLevelCombo.IsEnabled = c.AnisotropicFiltering;
            TessCombo.IsEnabled = TessLevelCombo.IsEnabled = c.Tessellation;
            TripleBufferCombo.IsEnabled = c.TripleBuffer;
            SurfaceFormatCombo.IsEnabled = c.SurfaceFormatOpt;
            EnhSyncCombo.IsEnabled = c.EnhancedSync; VSyncCombo.IsEnabled = c.VSync;
            ChillCombo.IsEnabled = c.Chill;
            FrtcCombo.IsEnabled = c.Frtc;
            RisCombo.IsEnabled = c.ImageSharpening;
            RsrCombo.IsEnabled = c.Rsr;
            BoostCombo.IsEnabled = c.Boost;
            var t = App.Hardware.Caps.Tuning;
            PowerCombo.IsEnabled = t.PowerSupported;
            PresetCombo.IsEnabled = t.PresetSupported;
            UpdateEditorVisibility();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Field_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        UpdateEditorVisibility();
        Changed();
    }

    /// <summary>Predefinições de um clique. Só mexe no que a GPU/driver suportam; o resto fica como está.</summary>
    private void Quick_Click(object sender, RoutedEventArgs e)
    {
        if (_app == null || sender is not Button { Tag: string key }) return;
        var c = App.Hardware.Caps.Graphics;
        var g = _app.Graphics;
        bool fsrOk = App.Hardware.CurrentGpu.Architecture.SupportsFsr4() && (c.Fsr || Services.Adl.DriverFsrService.IsSupported());

        void Reset()
        {
            _app.Graphics = g = new GraphicsSettings();
            _app.PowerLimitPercent = null;
            _app.TuningPreset = null;
        }

        switch (key)
        {
            case "clear":
                Reset();
                break;
            case "competitive": // menor latência possível, sem limite de FPS
                Reset();
                if (c.AntiLag) g.AntiLag = true;
                if (c.EnhancedSync) g.EnhancedSync = false;
                if (c.VSync) g.VSyncMode = (int)AdlxVSyncMode.AlwaysOff;
                if (c.Chill) g.Chill = false;
                if (c.Afmf) g.Afmf = false;
                break;
            case "fps60": // limitador em 60 FPS, o uso mais comum
                Reset();
                if (c.Chill) { g.Chill = true; g.ChillMinFps = 60; g.ChillMaxFps = 60; }
                if (c.AntiLag) g.AntiLag = true;
                break;
            case "saver": // menos consumo e calor
                Reset();
                if (c.Chill) { g.Chill = true; g.ChillMinFps = 60; g.ChillMaxFps = 90; }
                if (c.AntiLag) g.AntiLag = false;
                break;
            case "quality": // imagem mais nítida
                Reset();
                if (c.ImageSharpening) { g.ImageSharpening = true; g.ImageSharpeningLevel = DefRis; }
                if (fsrOk) g.Fsr = true;
                if (c.AntiLag) g.AntiLag = true;
                break;
        }

        Fill(_app);
        _saveDebounce?.Trigger();
    }

    private void Field_Toggled(object sender, RoutedEventArgs e) => Changed();

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _app == null) return;
        _app.Name = NameBox.Text;
        _saveDebounce?.Trigger();
    }

    private void Changed()
    {
        if (_loading || _app == null) return;
        var a = _app;
        a.Enabled = EnabledToggle.IsOn;
        a.KeepAfterExit = KeepCheck.IsChecked == true;
        var g = a.Graphics;
        g.AntiLag = TriToBool(AntiLagCombo.SelectedIndex);
        g.Afmf = TriToBool(AfmfCombo.SelectedIndex);
        g.Fsr = TriToBool(FsrCombo.SelectedIndex);
        g.EnhancedSync = TriToBool(EnhSyncCombo.SelectedIndex);
        g.Chill = TriToBool(ChillCombo.SelectedIndex);
        bool chillOn = ChillCombo.SelectedIndex == 1, risOn = RisCombo.SelectedIndex == 1,
             rsrOn = RsrCombo.SelectedIndex == 1, boostOn = BoostCombo.SelectedIndex == 1;
        int cmin = ChillMinEditor.Value, cmax = Math.Max(cmin, ChillMaxEditor.Value);
        g.ChillMinFps = chillOn ? cmin : null;
        g.ChillMaxFps = chillOn ? cmax : null;
        g.Frtc = null; // FRTC e global (aba Tuning)
        g.FrtcFps = null;
        g.ImageSharpening = TriToBool(RisCombo.SelectedIndex);
        g.ImageSharpeningLevel = risOn ? RisEditor.Value : null;
        g.Rsr = TriToBool(RsrCombo.SelectedIndex);
        g.RsrSharpness = rsrOn ? RsrEditor.Value : null;
        g.Boost = TriToBool(BoostCombo.SelectedIndex);
        g.BoostResolution = boostOn ? BoostEditor.Value : null;
        g.VSyncMode = VSyncCombo.SelectedItem is ComboBoxItem { Tag: int v } ? v : null;
        g.AaMode = SelTag(AaModeCombo);
        g.AaMethod = SelTag(AaMethodCombo);
        g.AaLevel = SelTag(AaLevelCombo);
        g.Mlaa = TriToBool(MlaaCombo.SelectedIndex);
        g.AnisotropicFiltering = TriToBool(AfCombo.SelectedIndex);
        g.AnisotropicLevel = AfCombo.SelectedIndex == 1 ? SelTag(AfLevelCombo) : null;
        g.TessellationMode = SelTag(TessCombo);
        g.TessellationLevel = g.TessellationMode == 2 ? SelTag(TessLevelCombo) : null;
        g.TripleBuffer = TriToBool(TripleBufferCombo.SelectedIndex);
        g.SurfaceFormatOpt = TriToBool(SurfaceFormatCombo.SelectedIndex);
        a.PowerLimitPercent = PowerCombo.SelectedIndex == 1 ? PowerEditor.Value : null;
        a.TuningPreset = PresetCombo.SelectedItem is ComboBoxItem { Tag: string s } ? s : null;

        // Chill e FRTC são mutuamente exclusivos no driver.
        if (g.Chill == true && g.Frtc == true)
        {
            g.Frtc = null;
            g.FrtcFps = null;
            _loading = true; FrtcCombo.SelectedIndex = 0; _loading = false;
            UpdateEditorVisibility();
        }

        _saveDebounce?.Trigger();
    }

    private void SaveNow()
    {
        _saveDebounce?.Cancel();
        App.SaveCurrentProfile();
        App.RestartEngine();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            IntPtr hwnd = App.MainWindowInstance != null ? WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance) : IntPtr.Zero;
            string filter = $"{L("Apps.FileFilter")}\0*.exe;*.lnk\0";
            string? file = NativeFileDialog.ShowOpenDialog(hwnd, filter, L("Apps.Add"));
            if (string.IsNullOrWhiteSpace(file)) return;
            var app = AddFromPath(file);
            if (app != null) SelectApp(app);
        }
        catch (Exception ex)
        {
            LogService.Error($"Adicionar app: {ex.Message}");
            Show(StatusBar, InfoBarSeverity.Error, L("Apps.AddFailed"), ex.Message);
        }
    }

    private static void SetIcon(Image img, AppProfile a, int size)
    {
        string? path = AppIconCache.GetPath(a);
        img.Source = path == null ? null : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path)) { DecodePixelWidth = size, DecodePixelHeight = size };
    }

    private UIElement BuildListEntry(AppProfile a)
    {
        var img = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        SetIcon(img, a, 24);
        var name = new TextBlock { Text = string.IsNullOrWhiteSpace(a.Name) ? a.ExeName : a.Name, TextTrimming = TextTrimming.CharacterEllipsis };
        var sub = new TextBlock { Text = a.ExeName, FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(name);
        texts.Children.Add(sub);
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(img);
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);
        if (ReferenceEquals(App.Engine.Active, a))
        {
            // Indicador de "em execução com perfil ativo".
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 9, Height = 9, Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x6C, 0xCB, 0x5F)), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(dot, L("Apps.ActiveDot"));
            Grid.SetColumn(dot, 2);
            row.Children.Add(dot);
        }

        return row;
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        if (e.DragUIOverride != null)
        {
            e.DragUIOverride.Caption = L("Apps.DropCaption");
            e.DragUIOverride.IsGlyphVisible = true;
        }
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        try
        {
            var dv = e.DataView;
            var paths = new List<string>();
            if (dv.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                foreach (var it in await dv.GetStorageItemsAsync())
                    if (!string.IsNullOrEmpty(it.Path)) paths.Add(it.Path);
            }
            else if (dv.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                string t = (await dv.GetTextAsync()).Trim().Trim('"');
                if (!string.IsNullOrEmpty(t)) paths.Add(t);
            }

            AppProfile? last = null;
            foreach (var path in paths) last = AddFromPath(path) ?? last;
            if (last != null) SelectApp(last);
        }
        catch (Exception ex)
        {
            LogService.Error($"Soltar arquivo: {ex.Message}");
            Show(StatusBar, InfoBarSeverity.Error, L("Apps.AddFailed"), ex.Message);
        }
    }

    private void SelectApp(AppProfile app)
    {
        _loading = true;
        RefreshList(app);
        _loading = false;
        _app = app;
        Fill(app);
    }

    /// <summary>Adiciona um .exe ou atalho .lnk. Retorna o perfil (novo ou existente) ou null se inválido.</summary>
    private AppProfile? AddFromPath(string file)
    {
        string ext = Path.GetExtension(file);
        if (ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            Show(StatusBar, InfoBarSeverity.Warning, L("Apps.UrlUnsupported"));
            return null;
        }

        string exe = ShortcutResolver.ResolveTarget(file);
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe))
        {
            Show(StatusBar, InfoBarSeverity.Warning, L("Apps.InvalidExe"));
            return null;
        }

        var existing = Apps.FirstOrDefault(a => string.Equals(a.ExecutablePath, exe, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Show(StatusBar, InfoBarSeverity.Informational, L("Apps.AlreadyAdded"));
            return existing;
        }

        var app = new AppProfile
        {
            Name = Path.GetFileNameWithoutExtension(exe),
            ExecutablePath = exe,
            IconBase64 = ShortcutResolver.ExtractIconBase64(exe)
        };
        Apps.Add(app);
        SaveNow();
        return app;
    }

    /// <summary>Aplica o perfil no mesmo caminho da ativação automática e mostra o que o driver respondeu.</summary>
    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_app == null) return;

        try
        {
            SaveNow();
            var report = App.Engine.ApplyNow(_app);
            ShowReport(StatusBar, report);
            UpdateActiveText();
        }
        catch (Exception ex)
        {
            LogService.Error($"Aplicar perfil agora: {ex.Message}");
            Show(StatusBar, InfoBarSeverity.Error, L("Apps.TestFailed"), ex.Message);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_app == null) return;
        Apps.Remove(_app);
        _app = null;
        SaveNow();
        RefreshList(null);
    }
}
