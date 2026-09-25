using System.Windows;

namespace YouTubeViewer;

public partial class MainWindow
{
    private static readonly TimeSpan NavigationDeadline = TimeSpan.FromSeconds(30);

    private Task InitializeSelectedTabAsync() =>
        _sessionReady && !_windowClosing && _webViewEnvironment is not null && CurrentTab is { } tab
            ? InitializeBrowserTabAsync(tab) : Task.CompletedTask;

    private void BeginTabNavigation(BrowserTab tab, ulong navigationId)
    {
        if (!_tabs.Contains(tab) || _windowClosing) return;
        CancelNavigationTimeout(tab);
        tab.NavigationId = navigationId;
        tab.LoadError = null;
        tab.IsLoading = true;
        tab.NavigationTimeout = new CancellationTokenSource();
        _ = WatchTabNavigationAsync(tab, navigationId, tab.NavigationTimeout.Token);
        UpdateChromeIfCurrent(tab);
    }

    private async Task WatchTabNavigationAsync(BrowserTab tab, ulong navigationId, CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(NavigationDeadline, cancellation);
            if (!cancellation.IsCancellationRequested) TimeoutTabNavigation(tab, navigationId);
        }
        catch (OperationCanceledException)
        {
            // Navigation completed, was replaced, or its tab was closed.
        }
    }

    private void MarkTabDocumentReady(BrowserTab tab, ulong navigationId)
    {
        if (_windowClosing || !_tabs.Contains(tab) || tab.NavigationId != navigationId || tab.LoadError is not null) return;
        // window.load waits for subresources too. A stalled image must not
        // make the watchdog stop and hide an already usable document/player.
        CancelNavigationTimeout(tab);
        tab.IsLoading = false;
        UpdateChromeIfCurrent(tab);
    }

    private void TimeoutTabNavigation(BrowserTab tab, ulong navigationId)
    {
        if (_windowClosing || !_tabs.Contains(tab) || !tab.IsLoading || tab.NavigationId != navigationId) return;
        SetTabLoadError(tab, $"Страница не ответила за {NavigationDeadline.TotalSeconds:0} секунд. Проверьте интернет, VPN или настройки подключения и повторите загрузку.");
        tab.View.CoreWebView2?.Stop();
    }

    private void SetTabLoadError(BrowserTab tab, string message)
    {
        CancelNavigationTimeout(tab);
        tab.IsLoading = false;
        tab.LoadError = message;
        UpdateChromeIfCurrent(tab);
    }

    private static void CancelNavigationTimeout(BrowserTab tab)
    {
        tab.NavigationTimeout?.Cancel();
        tab.NavigationTimeout?.Dispose();
        tab.NavigationTimeout = null;
    }

    private async Task ReloadCurrentTabAsync()
    {
        if (CurrentTab is not { } tab) return;
        var hadError = tab.LoadError is not null;
        tab.LoadError = null;
        tab.View.Visibility = Visibility.Visible;
        UpdateChrome();
        if (tab.View.CoreWebView2 is { } core)
        {
            if (hadError) core.Navigate(tab.LastAddress ?? tab.InitialAddress);
            else core.Reload();
        }
        else
        {
            if (tab.InitializationTask?.IsCompleted == true) tab.InitializationTask = null;
            await InitializeSelectedTabAsync();
        }
    }
}
