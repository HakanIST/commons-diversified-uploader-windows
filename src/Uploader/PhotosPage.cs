using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using UploaderCore;
using static Uploader.Ui;

namespace Uploader;

internal sealed partial class MainWindow
{
    private sealed record PhotoCard(StackPanel Root, TextBlock Title, Border Tag, TextBlock TagText);

    private static readonly (string Key, string Title, Func<PhotoRow, bool> Matches)[] Filters =
    [
        ("all", "Tümü", _ => true),
        ("found", "Oyuncu bulundu", r => r.Players.Count > 0),
        ("unresolved", "Çözülemeyen numara", r => r.Unresolved.Count > 0),
        ("none", "Oyuncu yok", r => r.Analyzed && r.Players.Count == 0),
        ("pending", "Analiz edilmedi", r => !r.Analyzed),
    ];

    private readonly Dictionary<string, PhotoCard> _cards = [];
    private readonly Dictionary<string, ToggleButton> _filterButtons = [];
    private GridView? _grid;
    private StackPanel? _inspector;
    private Button? _analyzeButton;
    private InfoBar? _issues;
    private string _filter = "all";
    private string? _selected;
    private string? _inspectorSignature;
    private bool _updatingGrid;

    private void BuildPhotosShell()
    {
        _photosPage.Children.Clear();
        _photosPage.RowDefinitions.Clear();
        _photosPage.ColumnDefinitions.Clear();
        _photosPage.RowDefinitions.Add(new RowDefinition { Height = Auto });
        _photosPage.RowDefinitions.Add(new RowDefinition { Height = Auto });
        _photosPage.RowDefinitions.Add(new RowDefinition { Height = Star });
        _photosPage.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
        _photosPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 12, 16, 8) };
        _analyzeButton = Btn("", () => _ = Run((p, ct) => _session.Analyze(null, p, ct), "Analiz başlatılamadı: {0}"), accent: true);
        bar.Children.Add(_analyzeButton);
        _filterButtons.Clear();
        foreach (var (key, _, _) in Filters)
        {
            var toggle = new ToggleButton { IsChecked = key == _filter };
            toggle.Click += (_, _) => { _filter = key; RefreshPhotos(); };
            _filterButtons[key] = toggle;
            bar.Children.Add(toggle);
        }
        _photosPage.Children.Add(bar.At(0, 0, 2));
        _issues = new InfoBar { Severity = InfoBarSeverity.Warning, IsClosable = false, IsOpen = false, Margin = new Thickness(16, 0, 16, 8) };
        _photosPage.Children.Add(_issues.At(1, 0, 2));

        _grid = new GridView { SelectionMode = ListViewSelectionMode.Single, Padding = new Thickness(16, 4, 8, 16) };
        _grid.SelectionChanged += (_, _) =>
        {
            if (_updatingGrid || (_grid.SelectedItem as FrameworkElement)?.Tag is not string name) return;
            _selected = name;
            BuildInspector(_session.Rows().FirstOrDefault(r => r.Name == name));
        };
        _photosPage.Children.Add(_grid.At(2, 0));

        _inspector = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        _inspectorSignature = null;
        var side = new Border
        {
            Child = new ScrollViewer { Content = _inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Background = Brush("LayerFillColorDefaultBrush"), BorderBrush = Brush("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1, 0, 0, 0),
        };
        _photosPage.Children.Add(side.At(2, 1));
    }

    private PhotoCard MakeCard(PhotoRow row)
    {
        var picture = new Image { Stretch = Stretch.UniformToFill, Width = 220, Height = 150 };
        var frame = new Border { Child = picture, CornerRadius = new CornerRadius(6), Width = 220, Height = 150, Background = Brush("ControlFillColorSecondaryBrush") };
        var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MaxWidth = 220 };
        var tagText = new TextBlock { FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        var tag = new Border { Child = tagText, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 1), Visibility = Visibility.Collapsed };
        var root = VStack(4, frame, title, HStack(6, Caption(row.Name), tag));
        root.Tag = row.Name;
        root.Width = 220;
        root.Margin = new Thickness(4);
        _ = LoadThumbnail(picture, Path.Combine(_session.Folder!, row.Name));
        return new PhotoCard(root, title, tag, tagText);
    }

    private static async Task LoadThumbnail(Image picture, string path)
    {
        if (await Thumbs.Thumbnail(path) is { } thumbnail) picture.Source = Picture(thumbnail);
    }

    private void RefreshPhotos()
    {
        if (_grid == null) BuildPhotosShell();
        var rows = _session.Rows();
        var matches = Filters.First(f => f.Key == _filter).Matches;
        var visible = new List<object>();
        foreach (var row in rows)
        {
            if (!_cards.TryGetValue(row.Name, out var card)) card = _cards[row.Name] = MakeCard(row);
            card.Title.Text = Session.Summary(row);
            card.Root.Opacity = row.Include ? 1 : 0.45;
            card.Tag.Visibility = row.Uploaded != null || row.UploadError != null ? Visibility.Visible : Visibility.Collapsed;
            card.TagText.Text = T(row.Uploaded != null ? "Yüklendi" : "Hata");
            card.Tag.Background = new SolidColorBrush(row.Uploaded != null ? Windows.UI.Color.FromArgb(255, 0x26, 0xa2, 0x69) : Windows.UI.Color.FromArgb(255, 0xc0, 0x1c, 0x28));
            if (matches(row)) visible.Add(card.Root);
        }
        _selected ??= Environment.GetEnvironmentVariable("CDU_SELECT") ?? rows.FirstOrDefault()?.Name;
        _updatingGrid = true;
        try
        {
            if (!_grid!.Items.SequenceEqual(visible))
            {
                _grid.Items.Clear();
                foreach (var item in visible) _grid.Items.Add(item);
            }
            var selected = _selected != null && _cards.TryGetValue(_selected, out var chosen) && visible.Contains(chosen.Root) ? chosen.Root : null;
            if (!ReferenceEquals(_grid.SelectedItem, selected)) _grid.SelectedItem = selected;
        }
        finally { _updatingGrid = false; }
        foreach (var (key, title, filter) in Filters)
        {
            _filterButtons[key].Content = $"{T(title)} {rows.Count(filter)}";
            _filterButtons[key].IsChecked = key == _filter;
        }
        // Kadro ya da forma rengi eksikse oyuncu bulunamaz; nedenini açıkça söyle.
        var issues = _session.SetupIssues();
        _issues!.Message = string.Join("\n", issues);
        _issues.IsOpen = issues.Count > 0;
        var pending = rows.Count(r => !r.Analyzed);
        _analyzeButton!.Content = pending > 0 ? T("Analiz et ({0})", pending) : T("Tümü analiz edildi");
        _analyzeButton.IsEnabled = pending > 0 && !_busy;
        BuildInspector(rows.FirstOrDefault(r => r.Name == _selected));
    }

    private void BuildInspector(PhotoRow? row)
    {
        // Yazarken odak kaybolmasın: içerik değişmediyse yeniden kurma.
        var signature = row == null ? "" : System.Text.Json.JsonSerializer.Serialize(row) + _busy;
        if (signature == _inspectorSignature) return;
        _inspectorSignature = signature;
        var box = _inspector!;
        box.Children.Clear();
        if (row == null)
        {
            box.Children.Add(Caption(T("Ayrıntı için bir fotoğraf seçin.")));
            return;
        }
        var record = _session.Project.Photos[row.Name];
        var path = Path.Combine(_session.Folder!, row.Name);

        var picture = new Image { Stretch = Stretch.Uniform, MaxHeight = 300 };
        box.Children.Add(new Border { Child = picture, CornerRadius = new CornerRadius(6), MinHeight = 200 });
        _ = LoadPreview(picture, path, row);

        var include = new CheckBox { Content = $"{T("Bu fotoğrafı yükle")} · {row.Name}{(row.Date != null ? " · " + row.Date : "")}", IsChecked = row.Include };
        include.Click += (_, _) => { record.Include = include.IsChecked == true; _session.Touch(); };
        box.Children.Add(include);

        var playersHeader = HStack(8, Caption(T(row.Manual ? "Oyuncular (elle düzenlendi)" : "Oyuncular")));
        if (row.Manual) playersHeader.Children.Add(new HyperlinkButton { Content = T("Otomatiğe dön"), Padding = new Thickness(4, 0, 4, 0) }.OnClick(() => { record.Manual = null; _session.Touch(); }));
        box.Children.Add(playersHeader);
        List<ManualPlayer> Current() => record.Manual ?? row.Players.Select(p => new ManualPlayer(p.Team, p.Number)).ToList();
        var teams = _session.Project.Match.Teams;
        var list = new StackPanel { Spacing = 4 };
        for (var i = 0; i < row.Players.Count; i++)
        {
            var player = row.Players[i];
            var index = i;
            var team = teams.FirstOrDefault(t => t.Key == player.Team)?.Label ?? player.Team;
            var details = new List<string> { team };
            if (player.Confidence is { } confidence) details.Add(T("güven %{0}", (int)(confidence * 100)));
            if (player.Category == null) details.Add(T("kategorisi yok"));
            var line = Columns(10, new GridLength(40), Star, Auto);
            var number = Txt(player.Number, "BodyStrongTextBlockStyle");
            number.VerticalAlignment = VerticalAlignment.Center;
            number.HorizontalAlignment = HorizontalAlignment.Center;
            line.Children.Add(number.At(0, 0));
            line.Children.Add(VStack(0, Txt(player.Name, "BodyStrongTextBlockStyle"), Caption(string.Join(" · ", details))).At(0, 1));
            var remove = new Button { Content = new SymbolIcon(Symbol.Remove), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(remove, T("Kaldır"));
            remove.Click += (_, _) => { var manual = Current(); manual.RemoveAt(index); record.Manual = manual; _session.Touch(); };
            line.Children.Add(remove.At(0, 2));
            list.Children.Add(Card(line, 8));
        }
        if (row.Players.Count == 0) list.Children.Add(Caption(T("Oyuncu yok")));
        box.Children.Add(list);

        var add = Columns(6, Auto, Star, Auto);
        var teamBox = new ComboBox { MinWidth = 130 };
        foreach (var t in teams) teamBox.Items.Add(t.Label.Length > 0 ? t.Label : t.Key.ToUpperInvariant());
        var playerBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        void FillPlayers()
        {
            playerBox.Items.Clear();
            if (teamBox.SelectedIndex < 0) return;
            foreach (var p in teams[teamBox.SelectedIndex].Players) playerBox.Items.Add($"{p.Number} {p.Name}");
            if (playerBox.Items.Count > 0) playerBox.SelectedIndex = 0;
        }
        teamBox.SelectionChanged += (_, _) => FillPlayers();
        if (teams.Count > 0) teamBox.SelectedIndex = 0;
        add.Children.Add(teamBox.At(0, 0));
        add.Children.Add(playerBox.At(0, 1));
        add.Children.Add(Btn(T("Ekle"), () =>
        {
            if (teamBox.SelectedIndex < 0 || playerBox.SelectedIndex < 0) return;
            var team = teams[teamBox.SelectedIndex];
            if (playerBox.SelectedIndex >= team.Players.Count) return;
            var manual = Current();
            var chosen = new ManualPlayer(team.Key, team.Players[playerBox.SelectedIndex].Number);
            if (!manual.Contains(chosen)) manual.Add(chosen);
            record.Manual = manual;
            _session.Touch();
        }).At(0, 2));
        box.Children.Add(add);

        var name = new TextBox { Header = T("Commons dosya adı"), Text = row.Title, TextWrapping = TextWrapping.Wrap };
        name.TextChanged += (_, _) => { var v = name.Text; record.Title = v.Length > 0 && v != row.Title ? v : record.Title; _session.Save(); };
        box.Children.Add(name);
        if (row.CustomTitle) box.Children.Add(new HyperlinkButton { Content = T("Şablondaki ada dön"), Padding = new Thickness(4, 0, 4, 0) }.OnClick(() => { record.Title = null; _session.Touch(); }));

        box.Children.Add(Caption(T("Kategoriler")));
        if (row.Categories.Count == 0) box.Children.Add(Txt(T("Kategori yok: maç ya da takım kategorisi girin."), null, Caution));
        foreach (var category in row.Categories) box.Children.Add(Txt("• " + category));

        if (row.Uploaded is { } uploaded)
        {
            if (uploaded.Url != null && Uri.TryCreate(uploaded.Url, UriKind.Absolute, out var link)) box.Children.Add(new HyperlinkButton { Content = uploaded.Title, NavigateUri = link });
            else box.Children.Add(Txt(uploaded.Title, null, Success));
        }
        if (row.UploadError is { } uploadError) box.Children.Add(Txt(uploadError, null, Critical));
        if (row.Error is { } error) box.Children.Add(Txt(T("Analiz hatası: {0}", error), null, Critical));
        var again = Btn(T("Yeniden analiz et"), () =>
        {
            record.Analysis = null;
            _ = Run((p, ct) => _session.Analyze([row.Name], p, ct), "Analiz başlatılamadı: {0}");
        });
        again.IsEnabled = !_busy;
        box.Children.Add(again);
    }

    private static async Task LoadPreview(Image picture, string path, PhotoRow row)
    {
        if (await Thumbs.Thumbnail(path) is { } quick) picture.Source = Picture(quick);
        if (await Thumbs.Preview(path, row) is { } preview) picture.Source = Picture(preview);
    }
}
