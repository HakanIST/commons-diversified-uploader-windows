using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using UploaderCore;

namespace Uploader;

/// <summary>Küçük arayüz yardımcıları.</summary>
internal static class Ui
{
    public static string T(string turkish, params object?[] args) => L.T(turkish, args);

    public static Brush? Brush(string key) => Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
    public static Brush? Secondary => Brush("TextFillColorSecondaryBrush");
    public static Brush? Success => Brush("SystemFillColorSuccessBrush");
    public static Brush? Critical => Brush("SystemFillColorCriticalBrush");
    public static Brush? Caution => Brush("SystemFillColorCautionBrush");

    public static TextBlock Txt(string text, string? style = null, Brush? brush = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (style != null && Application.Current.Resources.TryGetValue(style, out var value) && value is Style s) block.Style = s;
        if (brush != null) block.Foreground = brush;
        return block;
    }

    public static TextBlock Caption(string text) => Txt(text, "CaptionTextBlockStyle", Secondary);

    public static Button Btn(string text, Action onClick, bool accent = false)
    {
        var button = new Button { Content = text };
        if (accent && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var value) && value is Style s) button.Style = s;
        button.Click += (_, _) => onClick();
        return button;
    }

    public static StackPanel VStack(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = spacing };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static StackPanel HStack(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = spacing, Orientation = Orientation.Horizontal };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    /// <summary>Windows 11 ayar kartı görünümünde çerçeve.</summary>
    public static Border Card(UIElement child, double padding = 16) => new()
    {
        Child = child,
        Padding = new Thickness(padding),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        Background = Brush("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = Brush("CardStrokeColorDefaultBrush"),
    };

    public static TextBox Field(string header, string value, Action<string> changed, string? hint = null)
    {
        var box = new TextBox { Header = header, Text = value };
        if (hint != null) ToolTipService.SetToolTip(box, hint);
        box.TextChanged += (_, _) => changed(box.Text);
        return box;
    }

    /// <summary>Geniş ekranlarda içeriği ortalanmış, okunur genişlikte bir sütuna alır.</summary>
    public static ScrollViewer Page(FrameworkElement content, double maxWidth = 1100)
    {
        // Ortadaki sütun en çok maxWidth kadar genişler; artan alan iki yana eşit dağılır.
        var frame = new Grid { Padding = new Thickness(24) };
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1000, GridUnitType.Star), MaxWidth = maxWidth });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(content, 1);
        frame.Children.Add(content);
        return new ScrollViewer
        {
            Content = frame, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
    }

    public static T At<T>(this T element, int row, int column, int columnSpan = 1) where T : FrameworkElement
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
        return element;
    }

    public static Grid Columns(double spacing, params GridLength[] widths)
    {
        var grid = new Grid { ColumnSpacing = spacing };
        foreach (var width in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        return grid;
    }

    public static readonly GridLength Star = new(1, GridUnitType.Star);
    public static readonly GridLength Auto = GridLength.Auto;

    public static BitmapImage? Picture(string? path, int decodeWidth = 0)
    {
        if (path == null || !File.Exists(path)) return null;
        var image = new BitmapImage();
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.UriSource = new Uri(path);
        return image;
    }

    public static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    public static List<string> SplitList(string text) => text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
}

internal static class UiExtensions
{
    public static Microsoft.UI.Xaml.Controls.HyperlinkButton OnClick(this Microsoft.UI.Xaml.Controls.HyperlinkButton button, Action action)
    {
        button.Click += (_, _) => action();
        return button;
    }
}
