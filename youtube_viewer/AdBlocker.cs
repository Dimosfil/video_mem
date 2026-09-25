using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace YouTubeViewer;

internal sealed class AdBlocker
{
    private Task? _initialization;
    private CoreWebView2BrowserExtension? _extension;
    private CoreWebView2Environment? _environment;
    private IntPtr _parentWindow;
    public bool IsAvailable => _extension is not null && Error is null;
    public bool IsEnabled => IsAvailable && _extension!.IsEnabled;
    public string? Error { get; private set; }

    public Task InitializeAsync(CoreWebView2 core, IntPtr parentWindow) =>
        _initialization ??= InstallAsync(core, parentWindow);

    private async Task InstallAsync(CoreWebView2 core, IntPtr parentWindow)
    {
        try
        {
            var profile = core.Profile;
            _environment = core.Environment;
            _parentWindow = parentWindow;
            var directory = Path.Combine(AppContext.BaseDirectory, "Extensions", "uBlockOriginLite");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            var key = Convert.FromBase64String(manifest.RootElement.GetProperty("key").GetString()!);
            var hash = SHA256.HashData(key);
            var id = string.Concat(hash.Take(16).SelectMany(value => new[] { (char)('a' + (value >> 4)), (char)('a' + (value & 15)) }));
            var existing = (await profile.GetBrowserExtensionsAsync()).SingleOrDefault(item => item.Id == id);
            var enabled = existing?.IsEnabled ?? true;
            var installed = await profile.AddBrowserExtensionAsync(directory);
            if (installed.Id != id) throw new InvalidOperationException("Unexpected ad blocker identity.");
            await installed.EnableAsync(enabled);
            _extension = installed;
            if (enabled) await WaitUntilReadyAsync();
        }
        catch (Exception exception)
        {
            // Browsing still works if extensions are unavailable. Do not display
            // an active protection state when the extension failed to install.
            Error = exception.Message;
            _extension = null;
        }
    }

    public async Task ToggleAsync()
    {
        if (_extension is null) return;
        try
        {
            await _extension.EnableAsync(!_extension.IsEnabled);
            if (_extension.IsEnabled) await WaitUntilReadyAsync();
            Error = null;
        }
        catch (Exception exception)
        {
            Error = exception.Message;
            throw;
        }
    }

    private async Task WaitUntilReadyAsync()
    {
        // Enabling an extension returns before its worker finishes registering
        // filters. Probe it in a separate invisible controller so the first user
        // navigation is protected, without polluting that tab's history/session.
        var controller = await _environment!.CreateCoreWebView2ControllerAsync(_parentWindow);
        try
        {
            controller.IsVisible = false;
            controller.Bounds = new System.Drawing.Rectangle(0, 0, 1, 1);
            var probe = controller.CoreWebView2;
            probe.Navigate($"chrome-extension://{_extension!.Id}/dashboard.html");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                var ready = await probe.ExecuteScriptAsync("""
                    (() => {
                        if (!location.href.startsWith('chrome-extension:') || !chrome.runtime?.id) return false;
                        if (!window.viewerProbe) {
                            window.viewerProbe = 'pending';
                            chrome.runtime.sendMessage({what:'getOptionsPageData'}).then(async data => {
                                const scripts = await chrome.scripting.getRegisteredContentScripts();
                                window.viewerProbe = data.defaultFilteringMode >= 2 && data.hasOmnipotence && scripts.length > 0
                                    ? 'ready' : 'failed';
                            }).catch(() => window.viewerProbe = 'failed');
                        }
                        return window.viewerProbe === 'ready';
                    })()
                    """).WaitAsync(TimeSpan.FromSeconds(5));
                if (ready == "true") return;
                await Task.Delay(100);
            }
            throw new TimeoutException("Правила блокировки рекламы не готовы. Перезапустите Viewer.");
        }
        finally
        {
            controller.Close();
        }
    }
}
