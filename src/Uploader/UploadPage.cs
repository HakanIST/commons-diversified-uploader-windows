using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UploaderCore;
using static Uploader.Ui;

namespace Uploader;

internal sealed partial class MainWindow
{
    private OAuthRequest? _pendingLogin;
    private ContentDialog? _loginDialog;

    private void BuildUpload()
    {
        _uploadPage.Children.Clear();
        var rows = _session.Rows();
        var ready = Session.Ready(rows);
        var failed = rows.Where(r => r.UploadError != null).ToList();

        var account = VStack(10, Txt(T("Commons oturumu"), "SubtitleTextBlockStyle"));
        if (_session.Username is { } user)
        {
            var line = Columns(12, Auto, Star, Auto);
            line.Children.Add(new SymbolIcon(Symbol.Contact) { VerticalAlignment = VerticalAlignment.Center }.At(0, 0));
            var who = Txt(T("Commons oturumu: {0}", user), "BodyStrongTextBlockStyle");
            who.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(who.At(0, 1));
            line.Children.Add(Btn(T("Oturumu kapat"), _session.Logout).At(0, 2));
            account.Children.Add(line);
        }
        else
        {
            account.Children.Add(Txt(T("Wikimedia hesabınızla oturum açın. Parolanız uygulamaya verilmez; yalnızca dosya yükleme ve sayfa düzenleme izni istenir."), null, Secondary));
            var login = Btn(T("Commons ile giriş yap"), StartLogin, accent: true);
            login.Padding = new Thickness(20, 8, 20, 8);
            account.Children.Add(login);
        }
        _uploadPage.Children.Add(Card(account));

        var stats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 40 };
        void Stat(int value, string label, Brush? brush)
        {
            var number = new TextBlock { Text = value.ToString(), FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
            if (brush != null) number.Foreground = brush;
            stats.Children.Add(VStack(0, number, Caption(T(label))));
        }
        Stat(ready.Count, "yüklenecek", Brush("AccentTextFillColorPrimaryBrush"));
        Stat(rows.Count(r => r.Uploaded != null), "yüklendi", Success);
        Stat(failed.Count, "hatalı", Critical);
        Stat(rows.Count(r => !r.Include), "hariç tutuldu", Secondary);
        _uploadPage.Children.Add(stats);

        var actions = Columns(8, Auto, Star, Auto);
        actions.Children.Add(Btn(T("Kategorileri ve dosya adlarını denetle"), async () =>
        {
            try { _check = await _session.Check(); BuildUpload(); }
            catch (Exception e) { Alert(T("Denetim yapılamadı: {0}", e.Message)); }
        }).At(0, 0));
        var upload = Btn(T("{0} fotoğrafı yükle", ready.Count), () => ConfirmUpload(ready.Count), accent: true);
        upload.IsEnabled = _session.LoggedIn && ready.Count > 0 && !_busy;
        actions.Children.Add(upload.At(0, 2));
        _uploadPage.Children.Add(actions);

        if (_check is { } check)
        {
            var box = new StackPanel { Spacing = 4 };
            if (check.MissingCategories.Count == 0) box.Children.Add(Txt(T("Tüm kategoriler Commons'ta mevcut."), null, Success));
            else
            {
                box.Children.Add(Txt(T("Commons'ta bulunmayan kategoriler ({0}):", check.MissingCategories.Count), null, Caution));
                foreach (var c in check.MissingCategories) box.Children.Add(Txt("• " + c));
            }
            if (check.TakenTitles.Count == 0) box.Children.Add(Txt(T("Dosya adları kullanılabilir."), null, Success));
            else
            {
                box.Children.Add(Txt(T("Bu adlarla dosya zaten var ({0}):", check.TakenTitles.Count), null, Critical));
                foreach (var t in check.TakenTitles.Take(20)) box.Children.Add(Txt("• " + t));
            }
            _uploadPage.Children.Add(Card(box));
        }

        if (ready.FirstOrDefault() is { } first)
        {
            var text = new TextBlock { Text = _session.Wikitext(first), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            _uploadPage.Children.Add(Card(VStack(10, Txt(T("Örnek sayfa metni: {0}", first.Title), "BodyStrongTextBlockStyle"), text)));
        }

        if (failed.Count > 0)
        {
            var results = VStack(4, Txt(T("Sonuçlar"), "BodyStrongTextBlockStyle"));
            foreach (var r in failed) results.Children.Add(Txt($"{r.Name}: {r.UploadError}", null, Critical));
            _uploadPage.Children.Add(Card(results));
        }
    }

    private async void ConfirmUpload(int count)
    {
        var dialog = new ContentDialog
        {
            Title = T("{0} fotoğraf {1} hesabıyla Wikimedia Commons'a herkese açık olarak yüklenecek.", count, _session.Username),
            Content = new TextBlock { Text = T("Yüklenen dosyalar seçtiğiniz özgür lisansla yayımlanır; bu işlem geri alınamaz."), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = T("Yükle"), CloseButtonText = T("Vazgeç"), DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialog(dialog) == ContentDialogResult.Primary) _ = Run((p, ct) => _session.Upload(p, warning => Alert(warning), ct), "{0}");
    }

    // MARK: Oturum

    /// <summary>Yetkilendirme sayfasını varsayılan tarayıcıda açar; tarayıcı uygulamaya özel şemalı adresle döner.</summary>
    private async void StartLogin()
    {
        var request = _pendingLogin = OAuth.MakeRequest();
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(request.Url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) { _pendingLogin = null; Alert(e.Message); return; }

        var paste = new TextBox { PlaceholderText = OAuth.RedirectUri + "?code=…" };
        var dialog = new ContentDialog
        {
            Title = T("Commons ile giriş yap"),
            Content = VStack(10, Txt(T("Tarayıcıda izin verin; bu pencere kendiliğinden devam eder.")),
                Caption(T("Tarayıcı uygulamaya geri dönmediyse, adres çubuğundaki adresi buraya yapıştırın")), paste),
            PrimaryButtonText = T("Devam"), CloseButtonText = T("Vazgeç"),
        };
        _loginDialog = dialog;
        var result = await ShowDialog(dialog);
        if (_loginDialog == dialog) _loginDialog = null;
        if (_pendingLogin != request) return; // tarayıcıdan dönüldü, oturum tamamlanıyor
        if (result == ContentDialogResult.Primary && paste.Text.Trim() is { Length: > 0 } pasted) CompleteLogin(pasted);
        else _pendingLogin = null;
    }

    /// <summary>Tarayıcıdan dönen adresle oturumu tamamlar.</summary>
    public async void CompleteLogin(string callback)
    {
        if (_pendingLogin is not { } request) return;
        _pendingLogin = null;
        _loginDialog?.Hide();
        _loginDialog = null;
        try
        {
            var tokens = await OAuth.Exchange(OAuth.Code(callback, request), request);
            await _session.Login(new CommonsAuth.OAuth(OAuth.ClientId, tokens));
            _window.Activate();
        }
        catch (Exception e) { Alert(e.Message); }
    }
}
