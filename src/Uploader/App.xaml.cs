using Microsoft.UI.Xaml;

namespace Uploader;

public partial class App : Application
{
    internal static MainWindow? Window { get; private set; }

    public App()
    {
#if !TYPE_CHECK_ONLY
        InitializeComponent();
#endif
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Show();
    }
}
