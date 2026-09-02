using System.Runtime.InteropServices;
using System.Windows;

namespace YouTubeViewer;

public partial class App : Application
{
    private const string AppUserModelId = "Dimosfil.VideoMem.YouTubeViewer";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    protected override void OnStartup(StartupEventArgs e)
    {
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                args.Exception.Message,
                "YouTube Viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
