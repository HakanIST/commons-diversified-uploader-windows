using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UploaderCore;
using static Uploader.Ui;

namespace Uploader;

internal sealed partial class MainWindow
{
    private readonly Window _window = new();
    private readonly Session _session = new(new SecretStore());
    private readonly Grid _root = new();
    private readonly NavigationView _nav = new() { PaneDisplayMode = NavigationViewPaneDisplayMode.Top, IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed, IsSettingsVisible = false };
    private readonly StackPanel _matchPage = new() { Spacing = 18 }, _rosterPage = new() { Spacing = 18 }, _uploadPage = new() { Spacing = 18 };
    private readonly Grid _photosPage = new();
    private readonly Dictionary<string, UIElement> _pages = [];
    private readonly Grid _activityBar = Columns(12, Auto, Star, Auto);
    private readonly ProgressBar _progress = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _activityLabel = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520 };
    private readonly TextBlock _account = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly SemaphoreSlim _dialogGate = new(1);
    private FrameworkElement _splash = null!, _welcome = null!, _main = null!;
    private CancellationTokenSource? _work;
    private UploadCheck? _check;
    private bool _busy;

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);

    public MainWindow()
    {
        _window.Title = "Commons Diversified Uploader";
        _window.ExtendsContentIntoTitleBar = true;
        try { _window.SystemBackdrop = new MicaBackdrop(); } catch (Exception) { }
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var scale = Math.Max(1, GetDpiForWindow(handle) / 96.0);
        _window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1280 * scale), (int)(860 * scale)));
        try { _window.AppWindow.SetIcon(Asset("icon.ico")); } catch (Exception) { }
        if (Environment.GetEnvironmentVariable("CDU_MAXIMIZE") == "1" && _window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Maximize();

        // Başlık çubuğu: pencere sürüklenebilir alan
        var titleBar = HStack(10, new Image { Source = Picture(Asset("icon.png"), 40), Width = 18, Height = 18 }, Txt("Commons Diversified Uploader", "CaptionTextBlockStyle"));
        titleBar.Height = 40;
        titleBar.Padding = new Thickness(14, 0, 0, 0);
        foreach (var child in titleBar.Children.OfType<FrameworkElement>()) child.VerticalAlignment = VerticalAlignment.Center;
        _window.SetTitleBar(titleBar);

        foreach (var (key, title, symbol) in new[] { ("match", "Maç", Symbol.Flag), ("roster", "Kadrolar", Symbol.People), ("photos", "Fotoğraflar", Symbol.Pictures), ("upload", "Yükleme", Symbol.Upload) })
            _nav.MenuItems.Add(new NavigationViewItem { Content = T(title), Tag = key, Icon = new SymbolIcon(symbol) });
        _pages["match"] = Page(_matchPage);
        _pages["roster"] = Page(_rosterPage);
        _pages["photos"] = _photosPage;
        _pages["upload"] = Page(_uploadPage);
        var footer = HStack(12, _account, Btn(T("Klasör değiştir"), ChooseFolder));
        footer.Margin = new Thickness(0, 0, 12, 0);
        footer.VerticalAlignment = VerticalAlignment.Center;
        _nav.PaneFooter = footer;
        _nav.SelectionChanged += (_, _) => { _nav.Content = _pages.GetValueOrDefault(Step); RefreshCurrent(); };

        _activityBar.Padding = new Thickness(16, 10, 16, 10);
        _activityBar.Visibility = Visibility.Collapsed;
        _activityBar.Background = Brush("LayerFillColorDefaultBrush");
        _activityBar.Children.Add(_activityLabel.At(0, 0));
        _activityBar.Children.Add(_progress.At(0, 1));
        _activityBar.Children.Add(Btn(T("Durdur"), () => _work?.Cancel()).At(0, 2));

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = Star });
        main.RowDefinitions.Add(new RowDefinition { Height = Auto });
        main.Children.Add(_nav.At(0, 0));
        main.Children.Add(_activityBar.At(1, 0));
        _main = main;
        _splash = BuildSplash();
        _welcome = BuildWelcome();

        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        _root.RowDefinitions.Add(new RowDefinition { Height = Star });
        foreach (var layer in new[] { _main, _welcome }) { layer.Visibility = Visibility.Collapsed; _root.Children.Add(layer.At(1, 0)); }
        Grid.SetRowSpan(_splash, 2);
        _root.Children.Add(_splash);
        _root.Children.Add(titleBar.At(0, 0));
        _window.Content = _root;

        var context = SynchronizationContext.Current;
        _session.Changed += () => context?.Post(_ => { UpdateAccount(); RefreshCurrent(); }, null);
        _window.Closed += (_, _) => { _work?.Cancel(); _session.Dispose(); };
        UpdateAccount();
        _ = Start();
    }

    public void Show() => _window.Activate();

    private string Step => (_nav.SelectedItem as NavigationViewItem)?.Tag as string ?? "match";

    private void Select(string step) => _nav.SelectedItem = _nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == step) ?? _nav.MenuItems[0];

    private void ShowLayer(FrameworkElement layer)
    {
        foreach (var other in new[] { _splash, _welcome, _main }) other.Visibility = other == layer ? Visibility.Visible : Visibility.Collapsed;
    }

    // MARK: Açılış

    private readonly ProgressBar _splashProgress = new() { Width = 260, Maximum = 1, Margin = new Thickness(0, 14, 0, 0) };
    private readonly Image _splashIcon = new() { Width = 190, Height = 190, Opacity = 0 };
    private readonly TextBlock _splashTitle = new() { Text = "Commons Diversified Uploader", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _splashTagline = new() { Opacity = 0, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 15 };

    private FrameworkElement BuildSplash()
    {
        var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x24, 0x1f, 0x6b), Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x3a, 0x3f, 0xd0), Offset = 0.6 });
        gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x4a, 0x5b, 0xe6), Offset = 1 });
        var white = new SolidColorBrush(Colors.White);
        _splashIcon.Source = Picture(Asset("icon.png"), 380);
        _splashTitle.Foreground = white;
        _splashTagline.Foreground = white;
        _splashTagline.Text = T("Forma numarasından oyuncuya, oyuncudan kategoriye");
        _splashProgress.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xf6, 0xb5, 0x45));
        _splashProgress.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(64, 255, 255, 255));
        foreach (var element in new FrameworkElement[] { _splashIcon, _splashTitle, _splashTagline }) element.RenderTransform = new TranslateTransform();
        var box = VStack(14, _splashIcon, _splashTitle, _splashTagline, _splashProgress);
        box.VerticalAlignment = VerticalAlignment.Center;
        box.HorizontalAlignment = HorizontalAlignment.Center;
        var frame = new Grid { Background = gradient, RequestedTheme = ElementTheme.Dark };
        frame.Children.Add(box);
        return frame;
    }

    private static void Reveal(FrameworkElement element, double t, double start, double duration = 900, double rise = 18)
    {
        var x = Math.Clamp((t - start) / duration, 0, 1);
        var eased = 1 - Math.Pow(1 - x, 3);
        element.Opacity = eased;
        if (element.RenderTransform is TranslateTransform move) move.Y = (1 - eased) * rise;
    }

    /// <summary>Yaklaşık 3,6 saniyelik açılış: simge, başlık, alt yazı sırayla belirir; bu sırada oturum geri yüklenir.</summary>
    private async Task Start()
    {
        var restore = _session.RestoreLogin();
        const int total = 3600;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds <= total)
        {
            double t = clock.ElapsedMilliseconds;
            Reveal(_splashIcon, t, 150, rise: 0);
            Reveal(_splashTitle, t, 900);
            Reveal(_splashTagline, t, 1500, rise: 10);
            _splashProgress.Value = Math.Min(1, t / (total - 300));
            await Task.Delay(16);
        }
        try { await restore; } catch (Exception) { }
        UpdateAccount();
        if (Environment.GetEnvironmentVariable("CDU_FOLDER") is { Length: > 0 } folder && Directory.Exists(folder)) OpenFolder(folder);
        else ShowLayer(_welcome);
    }

    private FrameworkElement BuildWelcome()
    {
        var button = Btn(T("Klasör seç"), ChooseFolder, accent: true);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Padding = new Thickness(28, 10, 28, 10);
        var title = Txt(T("Maç fotoğraflarınızı Commons'a taşıyın"), "TitleTextBlockStyle");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var description = Txt(T("Bir maçın fotoğraflarını içeren klasörü seçin. Forma numaraları bu bilgisayarda okunur, oyuncular kadrodan bulunur ve Commons kategorileri önerilir."), null, Secondary);
        description.TextAlignment = TextAlignment.Center;
        description.MaxWidth = 520;
        var box = VStack(18, new Image { Source = Picture(Asset("icon.png"), 320), Width = 150, Height = 150 }, title, description, button);
        box.VerticalAlignment = VerticalAlignment.Center;
        box.HorizontalAlignment = HorizontalAlignment.Center;
        var frame = new Grid();
        frame.Children.Add(box);
        return frame;
    }

    // MARK: Klasör ve ortak işler

    private async void ChooseFolder()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
            if (await picker.PickSingleFolderAsync() is { } folder) OpenFolder(folder.Path);
        }
        catch (Exception e) { Alert(e.Message); }
    }

    private void OpenFolder(string folder)
    {
        try
        {
            _check = null;
            _cards.Clear();
            _grid = null;
            _selected = null;
            _session.Open(folder);
            ShowLayer(_main);
            Select(Environment.GetEnvironmentVariable("CDU_STEP") is { Length: > 0 } step ? step : "match");
            RefreshCurrent();
        }
        catch (Exception e) { Alert(T("Proje dosyası okunamadı: {0}", e.Message)); }
    }

    private void RefreshCurrent()
    {
        if (_session.Folder == null) return;
        switch (Step)
        {
            case "match": BuildMatch(); break;
            case "roster": BuildRoster(); break;
            case "photos": RefreshPhotos(); break;
            case "upload": BuildUpload(); break;
        }
    }

    private void UpdateAccount() => _account.Text = _session.Username ?? T("Oturum açılmadı");

    /// <summary>Aynı anda yalnızca bir iletişim kutusu açık olabilir; sıraya alınır.</summary>
    private async Task<ContentDialogResult> ShowDialog(ContentDialog dialog)
    {
        await _dialogGate.WaitAsync();
        try
        {
            dialog.XamlRoot = _root.XamlRoot;
            return await dialog.ShowAsync();
        }
        catch (Exception) { return ContentDialogResult.None; }
        finally { _dialogGate.Release(); }
    }

    private void Alert(string message) =>
        _ = ShowDialog(new ContentDialog { Title = T("Bir sorun oluştu"), Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, CloseButtonText = T("Tamam") });

    /// <summary>Uzun işi (analiz, yükleme) alttaki ilerleme çubuğuyla çalıştırır.</summary>
    private async Task Run(Func<IProgress<Activity>, CancellationToken, Task> work, string failure)
    {
        if (_busy) return;
        _busy = true;
        _work = new CancellationTokenSource();
        _activityBar.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
        var progress = new Progress<Activity>(a =>
        {
            _activityLabel.Text = a.Message;
            _progress.IsIndeterminate = a.Fraction == null;
            if (a.Fraction is { } f) { _progress.Maximum = 1; _progress.Value = f; }
        });
        try { await work(progress, _work.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) { Alert(T(failure, e.Message)); }
        finally
        {
            _busy = false;
            _activityBar.Visibility = Visibility.Collapsed;
            _work.Dispose();
            _work = null;
            RefreshCurrent();
        }
    }
}
