using System.IO;

namespace YouTubeViewer;

public partial class MainWindow
{
    private readonly BrowserSessionStore _sessionStore;
    private bool _sessionReady;
    private bool _windowClosing;

    private BrowserSession CaptureSession() => new(
        _tabs.Select(tab => tab.LastAddress ?? tab.InitialAddress).ToArray(),
        Math.Max(0, Tabs.SelectedIndex));

    private void SaveSession()
    {
        if (!_sessionReady || _windowClosing || _reorderingTabs) return;
        try
        {
            _sessionStore.Save(CaptureSession());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Не удалось сохранить вкладки: {exception.Message}");
        }
    }

    private BrowserTab[] RestoreSessionTabs()
    {
        var session = _sessionStore.Load();
        var addresses = session.Addresses.Length > 0 ? session.Addresses : new[] { BrowserAddress.Home };
        // Build every placeholder before asynchronous WebView initialization.
        // Closing during startup must preserve the entire session, not a prefix.
        var restored = addresses.Select(AddBrowserTab).ToArray();
        Tabs.SelectedItem = restored[Math.Clamp(session.SelectedIndex, 0, restored.Length - 1)].Item;
        _sessionReady = true;
        SaveSession();
        return restored;
    }

    private void RememberAddress(BrowserTab tab, string address)
    {
        if (!_tabs.Contains(tab) || !BrowserSession.IsWebAddress(address)) return;
        tab.LastAddress = address;
        SaveSession();
    }
}
