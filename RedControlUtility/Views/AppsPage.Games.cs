using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using RedControlUtility.Models;
using RedControlUtility.Services;
using static RedControlUtility.UiHelper;

namespace RedControlUtility.Views;

public sealed partial class AppsPage
{
    private static void LoadIcons(IEnumerable<string> paths, Action<string, string?> assign)
    {
        Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = 4 }, path =>
        {
            assign(path, AppIconCache.GetPath(new AppProfile { ExecutablePath = path }));
        });
    }

    private static Image IconImage(string? path, int size)
    {
        var img = new Image { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            img.Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = size, DecodePixelHeight = size };
        return img;
    }

    private static bool IsListed(string exe) => Apps.Any(a => string.Equals(a.ExecutablePath, exe, StringComparison.OrdinalIgnoreCase));

    private async void SearchGames_Click(object sender, RoutedEventArgs e)
    {
        SearchGamesButton.IsEnabled = false;
        try
        {
            Show(StatusBar, InfoBarSeverity.Informational, L("Apps.Scanning"));
            var found = await Task.Run(() =>
            {
                var list = GameLibraryService.Scan();
                LoadIcons(list.Select(g => g.ExecutablePath), (path, icon) =>
                {
                    var g = list.First(x => x.ExecutablePath == path);
                    g.IconPath = icon;
                });
                return list;
            });
            StatusBar.IsOpen = false;
            if (found.Count == 0)
            {
                Show(StatusBar, InfoBarSeverity.Warning, L("Apps.ScanNone"));
                return;
            }

            await ShowGamesDialogAsync(found);
        }
        catch (Exception ex)
        {
            LogService.Error($"Search games: {ex.Message}");
            Show(StatusBar, InfoBarSeverity.Error, L("Apps.AddFailed"), ex.Message);
        }
        finally
        {
            SearchGamesButton.IsEnabled = true;
        }
    }

    private async Task ShowGamesDialogAsync(List<GameEntry> games)
    {
        var selected = new HashSet<GameEntry>();
        bool populating = false;
        bool grid = string.Equals(App.Settings.Current.GamesViewMode, "Grid", StringComparison.OrdinalIgnoreCase);

        var filter = new TextBox { PlaceholderText = L("Apps.GamesFilter"), Width = 260 };
        var gridButton = new ToggleButton { Content = L("Apps.ViewGrid") };
        var listButton = new ToggleButton { Content = L("Apps.ViewList") };
        var selectAll = new Button { Content = L("Apps.SelectAll") };
        var selectNone = new Button { Content = L("Apps.SelectNone") };
        var count = new TextBlock { Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
        var listView = new ListView { SelectionMode = ListViewSelectionMode.Multiple, Height = 400 };
        var gridView = new GridView { SelectionMode = ListViewSelectionMode.Multiple, Height = 400 };

        var useGlobal = new CheckBox { Content = L("Apps.UseGlobal"), IsChecked = false };
        ToolTipService.SetToolTip(useGlobal, L("Apps.UseGlobalTip"));
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bar.Children.Add(filter);
        bar.Children.Add(gridButton);
        bar.Children.Add(listButton);
        bar.Children.Add(selectAll);
        bar.Children.Add(selectNone);

        var root = new StackPanel { Spacing = 10, MinWidth = 760 };
        root.Children.Add(new TextBlock { Text = L("Apps.GamesNote"), TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        root.Children.Add(bar);
        root.Children.Add(count);
        root.Children.Add(listView);
        root.Children.Add(gridView);
        root.Children.Add(useGlobal);

        var dlg = new ContentDialog
        { RequestedTheme = ThemeService.Element,
            XamlRoot = XamlRoot,
            Title = L("Apps.GamesTitle"),
            Content = root,
            PrimaryButtonText = L("Apps.AddSelected"),
            CloseButtonText = L("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };
        dlg.Resources["ContentDialogMaxWidth"] = 960.0;

        void UpdateState()
        {
            count.Text = L("Apps.GamesFound", games.Count, selected.Count);
            dlg.IsPrimaryButtonEnabled = selected.Count > 0;
        }

        UIElement Tile(GameEntry g, bool tile, bool listed)
        {
            if (tile)
            {
                var s = new StackPanel { Width = 128, Spacing = 4, Padding = new Thickness(4, 8, 4, 8), Opacity = listed ? 0.45 : 1 };
                s.Children.Add(IconImage(g.IconPath, 48));
                s.Children.Add(new TextBlock { Text = g.Name, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                s.Children.Add(new TextBlock { Text = listed ? L("Apps.AlreadyListed") : g.Launcher, FontSize = 11, Opacity = 0.6, TextAlignment = TextAlignment.Center });
                ToolTipService.SetToolTip(s, g.ExecutablePath);
                return s;
            }

            var row = new Grid { ColumnSpacing = 12, Opacity = listed ? 0.45 : 1 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(IconImage(g.IconPath, 28));
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = g.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            texts.Children.Add(new TextBlock { Text = g.ExecutablePath, FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            var tag = new TextBlock { Text = listed ? L("Apps.AlreadyListed") : g.Launcher, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tag, 2);
            row.Children.Add(tag);
            return row;
        }

        void Populate()
        {
            populating = true;
            try
            {
                var active = grid ? (ListViewBase)gridView : listView;
                listView.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
                gridView.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
                gridButton.IsChecked = grid;
                listButton.IsChecked = !grid;
                active.Items.Clear();
                string q = filter.Text.Trim();
                foreach (var g in games.Where(g => q.Length == 0 || g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || g.Launcher.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
                {
                    bool listed = IsListed(g.ExecutablePath);
                    SelectorItem item = grid
                        ? new GridViewItem { Content = Tile(g, true, listed), Tag = g, IsEnabled = !listed }
                        : new ListViewItem { Content = Tile(g, false, listed), Tag = g, IsEnabled = !listed };
                    active.Items.Add(item);
                    if (selected.Contains(g)) active.SelectedItems.Add(item);
                }
            }
            finally
            {
                populating = false;
            }

            UpdateState();
        }

        void OnSelection(object s, SelectionChangedEventArgs ev)
        {
            if (populating) return;
            foreach (var i in ev.AddedItems) if (i is SelectorItem { Tag: GameEntry g }) selected.Add(g);
            foreach (var i in ev.RemovedItems) if (i is SelectorItem { Tag: GameEntry g }) selected.Remove(g);
            UpdateState();
        }

        listView.SelectionChanged += OnSelection;
        gridView.SelectionChanged += OnSelection;
        filter.TextChanged += (_, _) => Populate();
        gridButton.Click += (_, _) => { grid = true; App.Settings.Current.GamesViewMode = "Grid"; Populate(); };
        listButton.Click += (_, _) => { grid = false; App.Settings.Current.GamesViewMode = "List"; Populate(); };
        selectAll.Click += (_, _) =>
        {
            foreach (var g in games.Where(g => !IsListed(g.ExecutablePath))) selected.Add(g);
            Populate();
        };
        selectNone.Click += (_, _) =>
        {
            selected.Clear();
            Populate();
        };

        Populate();
        if (await ThemeService.ShowAsync(dlg) != ContentDialogResult.Primary) return;

        AppProfile? last = null;
        int added = 0;
        foreach (var g in selected.Where(g => !IsListed(g.ExecutablePath)))
        {
            var app = NewApp(g.ExecutablePath, g.Name, g.Launcher);
            app.UseGlobal = useGlobal.IsChecked == true;
            Apps.Add(app);
            last = app;
            added++;
        }

        if (last == null) return;
        SaveNow();
        SelectApp(last);
        Show(StatusBar, InfoBarSeverity.Success, L("Apps.GamesAdded", added), L("Apps.RestartNote"));
    }

    private async void FromProcess_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var all = new List<RunningProcess>();
            var chosen = (RunningProcess?)null;
            var note = new RedControlUtility.Controls.Banner { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Title = L("Apps.ProcNoteTitle"), Message = L("Apps.RestartNote") };
            var filter = new TextBox { PlaceholderText = L("Apps.ProcFilter"), Width = 260 };
            var onlyWindows = new CheckBox { Content = L("Apps.ProcOnlyWindows"), IsChecked = true };
            var refresh = new Button { Content = L("Apps.ProcRefresh") };
            var list = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = 340 };
            var empty = new TextBlock { Text = L("Apps.ProcNone"), Opacity = 0.7, Visibility = Visibility.Collapsed };

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            bar.Children.Add(filter);
            bar.Children.Add(onlyWindows);
            bar.Children.Add(refresh);
            var root = new StackPanel { Spacing = 10, MinWidth = 720 };
            root.Children.Add(note);
            root.Children.Add(bar);
            root.Children.Add(list);
            root.Children.Add(empty);

            var dlg = new ContentDialog
            { RequestedTheme = ThemeService.Element,
                XamlRoot = XamlRoot,
                Title = L("Apps.ProcTitle"),
                Content = root,
                PrimaryButtonText = L("Apps.ProcAdd"),
                CloseButtonText = L("Common.Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false
            };
            dlg.Resources["ContentDialogMaxWidth"] = 960.0;

            void Fill()
            {
                list.Items.Clear();
                string q = filter.Text.Trim();
                foreach (var p in all.Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || p.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
                {
                    bool listed = IsListed(p.ExecutablePath);
                    var row = new Grid { ColumnSpacing = 12, Opacity = listed ? 0.45 : 1 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(IconImage(p.IconPath, 28));
                    var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    texts.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(p.Title) ? p.Name : $"{p.Name}  -  {p.Title}", TextTrimming = TextTrimming.CharacterEllipsis });
                    texts.Children.Add(new TextBlock { Text = p.ExecutablePath, FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
                    Grid.SetColumn(texts, 1);
                    row.Children.Add(texts);
                    if (listed)
                    {
                        var tag = new TextBlock { Text = L("Apps.AlreadyListed"), Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center };
                        Grid.SetColumn(tag, 2);
                        row.Children.Add(tag);
                    }

                    list.Items.Add(new ListViewItem { Content = row, Tag = p, IsEnabled = !listed });
                }

                empty.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                chosen = null;
                dlg.IsPrimaryButtonEnabled = false;
            }

            async Task Reload()
            {
                refresh.IsEnabled = false;
                bool only = onlyWindows.IsChecked == true;
                all = await Task.Run(() =>
                {
                    var l = ProcessListService.List(only);
                    LoadIcons(l.Select(x => x.ExecutablePath).Distinct(StringComparer.OrdinalIgnoreCase), (path, icon) =>
                    {
                        foreach (var p in l.Where(x => string.Equals(x.ExecutablePath, path, StringComparison.OrdinalIgnoreCase))) p.IconPath = icon;
                    });
                    return l;
                });
                Fill();
                refresh.IsEnabled = true;
            }

            list.SelectionChanged += (_, _) =>
            {
                chosen = (list.SelectedItem as ListViewItem)?.Tag as RunningProcess;
                dlg.IsPrimaryButtonEnabled = chosen != null;
            };
            filter.TextChanged += (_, _) => Fill();
            refresh.Click += async (_, _) => await Reload();
            onlyWindows.Click += async (_, _) => await Reload();

            await Reload();
            if (await ThemeService.ShowAsync(dlg) != ContentDialogResult.Primary || chosen == null) return;

            var app = NewApp(chosen.ExecutablePath, chosen.Name, "Process");
            Apps.Add(app);
            SaveNow();
            SelectApp(app);
            Show(StatusBar, InfoBarSeverity.Warning, L("Apps.ProcAdded", app.Name), L("Apps.RestartNote"));
        }
        catch (Exception ex)
        {
            LogService.Error($"Add from process: {ex.Message}");
            Show(StatusBar, InfoBarSeverity.Error, L("Apps.AddFailed"), ex.Message);
        }
    }
}
