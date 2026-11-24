using UploaderCore;
using Xunit;

public class CoreTests
{
    private static List<Team> Teams() =>
    [
        new Team { Key = "a", Label = "Fenerbahçe", Colors = ["sarı", "lacivert"], Keywords = ["Fenerbahçe", "tarfin"], Players = [new Player { Number = "31", Name = "Shavon Shields", Qid = "Q1", Category = "Shavon Shields" }, new Player { Number = "4", Name = "Nicolò Melli", Category = "Nicolò Melli" }] },
        new Team { Key = "b", Label = "Beşiktaş", Colors = ["siyah"], Keywords = ["Beşiktaş", "fuzul"], Players = [new Player { Number = "41", Name = "Ante Žižić", Category = "Ante Žižić" }, new Player { Number = "4", Name = "Lonnie Walker IV", Category = "Lonnie Walker" }] },
    ];

    private static NumberDetection Det(string number, Dictionary<string, double> colors, double y = 0.5, double h = 0.06, int hits = 2) =>
        new(number, new NormalizedBox(0.4, y, 0.46, y + h), hits, h, colors);

    private static PhotoAnalysis Photo(List<NumberDetection> numbers, List<TextDetection>? texts = null) => new(3000, 2000, numbers, texts ?? []);

    [Fact]
    public void ColorPicksTeamAndNumberPicksPlayer()
    {
        var a = Photo([Det("31", new() { ["lacivert"] = 0.39, ["sarı"] = 0.33, ["beyaz"] = 0.2 }), Det("41", new() { ["siyah"] = 0.65, ["beyaz"] = 0.28 }, y: 0.2)]);
        Assert.Equal(["Ante Žižić", "Shavon Shields"], Matcher.Match(a, Teams()).Players.Select(p => p.Name).Order());
    }

    [Fact]
    public void SameNumberOnBothTeamsIsSplitByColor()
    {
        var a = Photo([Det("4", new() { ["siyah"] = 0.64, ["beyaz"] = 0.32 })]);
        Assert.Equal("Lonnie Walker IV", Matcher.Match(a, Teams()).Players.Single().Name);
    }

    [Fact]
    public void TinyAndUnknownNumbersAreDropped()
    {
        var r = Matcher.Match(Photo([Det("0", new() { ["turuncu"] = 1 }, h: 0.0006), Det("50", new() { ["siyah"] = 0.7 })]), Teams());
        Assert.Empty(r.Players);
        Assert.Equal(["50"], r.Unresolved.Select(u => u.Number));
    }

    [Fact]
    public void JerseyTextDecidesWhenColorIsUnclear()
    {
        var a = Photo([Det("41", new() { ["gri"] = 0.6, ["siyah"] = 0.2 }, hits: 1)], [new TextDetection("BESIKTAS", new NormalizedBox(0.38, 0.42, 0.5, 0.47))]);
        Assert.Equal("Ante Žižić", Matcher.Match(a, Teams()).Players.Single().Name);
    }

    [Fact]
    public void SingleSightingWithoutJerseyTextIsIgnored()
    {
        Assert.Empty(Matcher.Match(Photo([Det("41", new() { ["siyah"] = 0.8 }, h: 0.09, hits: 1)]), Teams()).Players);
        var withText = Photo([Det("41", new() { ["siyah"] = 0.8 }, h: 0.0204, hits: 1)], [new TextDetection("fuzul", new NormalizedBox(0.39, 0.53, 0.47, 0.55))]);
        Assert.Single(Matcher.Match(withText, Teams()).Players);
    }

    [Fact]
    public void KeywordMatchingToleratesOcrErrors()
    {
        Assert.True(Matcher.KeywordHit("BESKTAR", ["Beşiktaş"]));
        Assert.True(Matcher.KeywordHit("TARfin", ["tarfin"]));
        Assert.False(Matcher.KeywordHit("umbro", ["Beşiktaş", "fuzul"]));
    }

    [Fact]
    public void TitleAndWikitext()
    {
        List<MatchedPlayer> players = [new MatchedPlayer { Team = "a", Number = "31", Name = "Shavon Shields" }];
        const string template = "{oyuncular} {numaralar} - {maç} ({sıra})";
        Assert.Equal("Shavon Shields 31 - Beşiktaş vs Fenerbahçe 20261122 (7).jpg", PageBuilder.Title(template, players, "Beşiktaş vs Fenerbahçe 20261122", 7, "jpg"));
        Assert.Equal("A vs B (2).jpg", PageBuilder.Title(template, [], "A vs B", 2, "jpg"));
        var w = PageBuilder.Wikitext(PageBuilder.Describe(players, "maç", "tr"), "", "2026-11-22 20:28:30", "[[User:X|X]]", "cc-by-sa-4.0", ["Shavon Shields", "Shavon Shields", "M"]);
        Assert.Contains("{{self|cc-by-sa-4.0}}", w);
        Assert.Contains("|description={{tr|1=Shavon Shields, maç}}", w);
        Assert.Contains("|source={{own}}", w);
        Assert.Equal(2, w.Split("[[Category:").Length - 1);
        Assert.StartsWith("=={{int:filedesc}}==\n{{Information\n", w);
    }

    [Fact]
    public void RosterParsing()
    {
        var r = Wikidata.ParseRoster("* 31 [[Shavon Shields|Shields]]\n* 10 [[Melih Mahmutoğlu|Mahmutoğlu]] '''(K)'''\n{{TBL oyuncu | num =  0 | nat = TUR| ilk = Shane | son = Larkin| pos = PG}}\n{{player2 | num = 7 | first = Ali | last = Veli | dab = basketball }}");
        Assert.Equal(["0", "7", "10", "31"], r.Select(p => p.Number));
        Assert.Equal(["Shane Larkin", "Ali Veli", "Melih Mahmutoğlu", "Shavon Shields"], r.Select(p => p.Name));
        Assert.Equal("Ali Veli (basketball)", r[1].Title);
    }

    [Fact]
    public void TilesAndColors()
    {
        var t = ImageAnalyzer.Tiles(3000, 2000, 3, 2);
        Assert.Equal(7, t.Count);
        Assert.All(t, x => Assert.True(x.X >= 0 && x.X + x.Width <= 3000 && x.Y + x.Height <= 2000));
        Assert.Equal("sarı", JerseyColor.Name(250, 230, 20));
        Assert.Equal("lacivert", JerseyColor.Name(15, 30, 90));
        Assert.Equal("siyah", JerseyColor.Name(10, 10, 10));
    }

    [Fact]
    public void NoRepeatNgram()
    {
        Assert.Equal([7L], FlorenceOcr.BannedTokens([5, 6, 7, 9, 5, 6]));
        Assert.Empty(FlorenceOcr.BannedTokens([5, 6]));
    }

    [Fact]
    public void ProjectRoundTripUsesSharedJsonShape()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var p = new Project();
            p.Match.Teams = Teams();
            p.Sync(["a.jpg", "b.jpg"]);
            p.Photos["a.jpg"].Manual = [new ManualPlayer("a", "31")];
            p.Photos["a.jpg"].Analysis = Photo([Det("31", new() { ["sarı"] = 0.5 })]);
            p.Save(dir);
            var json = File.ReadAllText(Path.Combine(dir, Project.FileName));
            // macOS uygulamasının yazdığı anahtar adlarıyla aynı olmalı.
            Assert.Contains("\"heightRatio\"", json);
            Assert.Contains("\"x0\"", json);
            Assert.Contains("\"titleTemplate\"", json);
            var loaded = Project.Load(dir);
            Assert.Equal(2, loaded.Photos.Count);
            Assert.Equal(new ManualPlayer("a", "31"), loaded.Photos["a.jpg"].Manual!.Single());
            Assert.Equal("31", loaded.Photos["a.jpg"].Analysis!.Numbers.Single().Number);
            Assert.True(loaded.Photos["b.jpg"].Include);
        }
        finally { Directory.Delete(dir, true); }
    }
}
