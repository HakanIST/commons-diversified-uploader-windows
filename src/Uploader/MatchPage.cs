using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using UploaderCore;
using static Uploader.Ui;

namespace Uploader;

internal sealed partial class MainWindow
{
    private static readonly Dictionary<string, uint> Swatches = new()
    {
        ["siyah"] = 0x111111, ["beyaz"] = 0xffffff, ["gri"] = 0x8e8e93, ["kırmızı"] = 0xd92e21, ["turuncu"] = 0xff9500, ["sarı"] = 0xfcdb33,
        ["yeşil"] = 0x17a34a, ["turkuaz"] = 0x32ade6, ["mavi"] = 0x2663eb, ["lacivert"] = 0x1f2978, ["mor"] = 0xaf52de, ["pembe"] = 0xff2d55, ["bordo"] = 0x7a1f2b,
    };
    private static readonly string[] Licenses = ["cc-by-sa-4.0", "cc-by-4.0", "cc-zero"];

    private static Brush Swatch(string color)
    {
        var rgb = Swatches.GetValueOrDefault(color, 0x808080u);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }

    private void BuildMatch()
    {
        _matchPage.Children.Clear();
        var match = _session.Project.Match;

        var folderText = VStack(2, Txt(Path.GetFileName(_session.Folder!.TrimEnd(Path.DirectorySeparatorChar)), "BodyStrongTextBlockStyle"),
            Caption($"{Path.GetDirectoryName(_session.Folder)} · {T("{0} fotoğraf", _session.Project.Photos.Count)}"));
        var folderIcon = new SymbolIcon(Symbol.Folder) { VerticalAlignment = VerticalAlignment.Center };
        _matchPage.Children.Add(Card(HStack(14, folderIcon, folderText)));

        var teams = Columns(18, Star, Star);
        for (var i = 0; i < match.Teams.Count && i < 2; i++) teams.Children.Add(TeamCard(match.Teams[i], i).At(0, i));
        _matchPage.Children.Add(teams);

        var details = VStack(12, Txt(T("Maç ve yükleme bilgileri"), "SubtitleTextBlockStyle"));
        details.Children.Add(Field(T("Maç başlığı"), match.Title, v => { match.Title = v; _session.Save(); }, T("Dosya adında ve açıklamada kullanılır")));
        details.Children.Add(Field(T("Maç kategorisi"), match.Category, v => { match.Category = v; _session.Save(); }, T("Boşsa takım kategorileri kullanılır")));
        var create = new ToggleSwitch { Header = T("Maç kategorisi yoksa oluştur"), IsOn = match.CreateCategory };
        ToolTipService.SetToolTip(create, T("Commons'ta yoksa takım kategorilerinin altında oluşturulur"));
        create.Toggled += (_, _) => { match.CreateCategory = create.IsOn; _session.Save(); };
        details.Children.Add(create);
        details.Children.Add(Field(T("Diğer kategoriler (virgülle)"), string.Join(", ", match.ExtraCategories), v => { match.ExtraCategories = SplitList(v); _session.Save(); }));
        details.Children.Add(Field(T("Dosya adı şablonu"), match.TitleTemplate, v => { match.TitleTemplate = v; _session.Save(); }, T("Alanlar: {oyuncular} {numaralar} {maç} {sıra}")));
        details.Children.Add(Field(T("Yazar"), match.Author, v => { match.Author = v; _session.Save(); }, T("Boşsa Commons kullanıcı adınız")));
        var license = new ComboBox { Header = T("Lisans"), MinWidth = 240 };
        foreach (var name in new[] { "CC BY-SA 4.0", "CC BY 4.0", T("CC0 (kamu malı)") }) license.Items.Add(name);
        license.SelectedIndex = Math.Max(0, Array.IndexOf(Licenses, match.License));
        license.SelectionChanged += (_, _) => { if (license.SelectedIndex >= 0) { match.License = Licenses[license.SelectedIndex]; _session.Save(); } };
        details.Children.Add(license);
        _matchPage.Children.Add(Card(details));
    }

    private Border TeamCard(Team team, int index)
    {
        var box = VStack(12, Txt(team.Label.Length > 0 ? team.Label : T("{0}. takım", index + 1), "SubtitleTextBlockStyle"),
            Caption(T("{0} oyuncu · {1}", team.Players.Count, team.Category.Length > 0 ? team.Category : T("Commons kategorisi yok"))));

        var results = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        var query = new TextBox { PlaceholderText = T("Takım ara (ör. Fenerbahçe basketbol)") };
        async void Search()
        {
            if (query.Text.Trim().Length == 0) return;
            try
            {
                var hits = await Wikidata.Search(query.Text, L.IsTurkish ? "tr" : "en");
                results.Children.Clear();
                if (hits.Count == 0) results.Children.Add(Caption(T("Sonuç yok")));
                foreach (var hit in hits)
                {
                    var item = new Button
                    {
                        Content = VStack(0, Txt(hit.Label, "BodyStrongTextBlockStyle"), Caption($"{hit.Description} · {hit.Qid}")),
                        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    };
                    item.Click += async (_, _) =>
                    {
                        results.Visibility = Visibility.Collapsed;
                        try { await _session.SetTeam(team.Key, hit.Qid); }
                        catch (Exception e) { Alert(T("Kadro getirilemedi: {0}", e.Message)); results.Visibility = Visibility.Visible; }
                    };
                    results.Children.Add(item);
                }
                results.Visibility = Visibility.Visible;
            }
            catch (Exception e) { Alert(T("Arama yapılamadı: {0}", e.Message)); }
        }
        query.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) Search(); };
        var searchRow = Columns(8, Star, Auto);
        searchRow.Children.Add(query.At(0, 0));
        searchRow.Children.Add(Btn(T("Ara"), Search).At(0, 1));
        box.Children.Add(searchRow);
        box.Children.Add(results);

        box.Children.Add(Caption(T("Bu maçtaki forma renkleri")));
        const int perRow = 4;
        var colors = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (var c = 0; c < perRow; c++) colors.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
        for (var r = 0; r < (JerseyColor.Names.Length + perRow - 1) / perRow; r++) colors.RowDefinitions.Add(new RowDefinition { Height = Auto });
        for (var i = 0; i < JerseyColor.Names.Length; i++)
        {
            var color = JerseyColor.Names[i];
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 14, Height = 14, Fill = Swatch(color), Stroke = Secondary, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center };
            var toggle = new ToggleButton { Content = HStack(6, dot, new TextBlock { Text = T(color) }), IsChecked = team.Colors.Contains(color), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            toggle.Click += (_, _) =>
            {
                team.Colors.Remove(color);
                if (toggle.IsChecked == true) team.Colors.Add(color);
                _session.Save();
            };
            colors.Children.Add(toggle.At(i / perRow, i % perRow));
        }
        box.Children.Add(colors);
        box.Children.Add(Field(T("Formadaki yazılar (takım adı, sponsor; virgülle)"), string.Join(", ", team.Keywords), v => { team.Keywords = SplitList(v); _session.Save(); }));
        box.Children.Add(Field(T("Takımın Commons kategorisi"), team.Category, v => { team.Category = v; _session.Save(); }));
        var card = Card(box);
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    // MARK: Kadrolar

    private void BuildRoster()
    {
        _rosterPage.Children.Clear();
        var columns = Columns(18, Star, Star);
        var column = 0;
        foreach (var team in _session.Project.Match.Teams.Take(2))
        {
            var header = Columns(8, Star, Auto, Auto);
            header.Children.Add(Txt(team.Label.Length > 0 ? team.Label : T("Takım seçilmedi"), "SubtitleTextBlockStyle").At(0, 0));
            if (team.Qid is { } qid)
            {
                header.Children.Add(Btn(T("Vikipedi'den yenile"), async () =>
                {
                    try { await _session.SetTeam(team.Key, qid); } catch (Exception e) { Alert(T("Kadro getirilemedi: {0}", e.Message)); }
                }).At(0, 1));
            }
            header.Children.Add(Btn(T("Oyuncu ekle"), () => { team.Players.Add(new Player()); _session.Touch(); }).At(0, 2));
            var box = VStack(8, header);
            if (team.RosterSource is { } source) box.Children.Add(Caption(T("Kaynak: {0}.wikipedia.org — {1}. Maç günündeki kadroyla karşılaştırıp düzeltin.", source.Lang, source.Page)));
            else if (team.Players.Count == 0) box.Children.Add(Caption(T("Kadro boş. \"Maç\" adımından takımı seçin ya da oyuncuları elle ekleyin.")));

            foreach (var player in team.Players)
            {
                var line = Columns(8, new GridLength(64), Star, Star, Auto);
                TextBox Cell(string placeholder, string value, Action<string> changed)
                {
                    var entry = new TextBox { PlaceholderText = placeholder, Text = value };
                    entry.TextChanged += (_, _) => { changed(entry.Text); _session.Save(); };
                    return entry;
                }
                line.Children.Add(Cell(T("No"), player.Number, v => player.Number = v.Trim()).At(0, 0));
                line.Children.Add(Cell(T("Ad Soyad"), player.Name, v => player.Name = v).At(0, 1));
                line.Children.Add(Cell(T("Commons kategorisi"), player.Category ?? "", v => player.Category = v.Length > 0 ? v : null).At(0, 2));
                var remove = new Button { Content = new SymbolIcon(Symbol.Remove) };
                ToolTipService.SetToolTip(remove, T("Kaldır"));
                remove.Click += (_, _) => { team.Players.Remove(player); _session.Touch(); };
                line.Children.Add(remove.At(0, 3));
                box.Children.Add(line);
            }
            var card = Card(box);
            card.VerticalAlignment = VerticalAlignment.Top;
            columns.Children.Add(card.At(0, column++));
        }
        _rosterPage.Children.Add(columns);
    }
}
