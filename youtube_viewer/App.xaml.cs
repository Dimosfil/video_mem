using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace YouTubeViewer;

public partial class App : Application
{
    private const string AppUserModelId = "Dimosfil.VideoMem.YouTubeViewer";
    private ViewerInstance? _instance;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    protected override async void OnStartup(StartupEventArgs e)
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

        await WaitForPreviousInstanceAsync(e.Args);

        _instance = new ViewerInstance();
        if (!_instance.TryAcquire())
        {
            ActivateExistingWindow();
            Shutdown();
            return;
        }

        MainWindow = new MainWindow();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void ActivateExistingWindow()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.Id == current.Id || process.MainWindowHandle == IntPtr.Zero) continue;
                    var handle = process.MainWindowHandle;
                    if (IsIconic(handle)) ShowWindowAsync(handle, 9); // SW_RESTORE
                    SetForegroundWindow(handle);
                    return;
                }
                catch (InvalidOperationException)
                {
                    // The previous window may be finishing shutdown.
                }
            }
        }
    }

    private static async Task WaitForPreviousInstanceAsync(string[] arguments)
    {
        var optionIndex = Array.IndexOf(arguments, "--wait-for-pid");
        if (optionIndex < 0 || optionIndex + 1 >= arguments.Length ||
            !int.TryParse(arguments[optionIndex + 1], out var processId))
        {
            return;
        }

        try
        {
            using var previousProcess = Process.GetProcessById(processId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await previousProcess.WaitForExitAsync(timeout.Token);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            // The previous process already exited or timed out. Startup can continue safely.
        }
    }
}
