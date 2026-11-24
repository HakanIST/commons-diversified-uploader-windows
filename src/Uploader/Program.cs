using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using UploaderCore;
using Windows.ApplicationModel.Activation;

namespace Uploader;

public static class Program
{
    private static DispatcherQueue? _ui;

    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // Tek örnek: tarayıcı oturum onayından sonra uygulamayı özel şemalı adresle açar; çalışan örneğe iletilir.
        var main = AppInstance.FindOrRegisterForKey("main");
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (!main.IsCurrent)
        {
            var redirect = new Thread(() => main.RedirectActivationToAsync(activation).AsTask().Wait());
            redirect.Start();
            redirect.Join();
            return 0;
        }
        main.Activated += (_, e) => Handle(e);

        Http.UserAgent = "CommonsDiversifiedUploader/1.0 (Windows; native app)";
        if (Environment.GetEnvironmentVariable("CDU_LANG") is { Length: > 0 } language) L.Language = language;
        Strings.Register();

        Application.Start(parameters =>
        {
            _ui = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(_ui));
            _ = new App();
        });
        return 0;
    }

    private static void Handle(AppActivationArguments e)
    {
        var uri = e.Kind == ExtendedActivationKind.Protocol && e.Data is IProtocolActivatedEventArgs protocol ? protocol.Uri?.AbsoluteUri : null;
        _ui?.TryEnqueue(() =>
        {
            if (App.Window is not { } window) return;
            window.Show();
            if (uri != null && uri.StartsWith(OAuth.CallbackScheme + ":", StringComparison.OrdinalIgnoreCase)) window.CompleteLogin(uri);
        });
    }
}
