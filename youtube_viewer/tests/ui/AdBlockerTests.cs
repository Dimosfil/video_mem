using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

internal static partial class Program
{
    private static async Task VerifyAdBlockerAsync(CoreWebView2 core, IntPtr parentWindow)
    {
        var extensions = await core.Profile.GetBrowserExtensionsAsync();
        Console.WriteLine("Extensions: " + string.Join(", ", extensions.Select(item => $"{item.Id} {item.Name} enabled={item.IsEnabled}")));
        var blockers = extensions.Where(item => item.Name == "uBlock Origin Lite").ToArray();
        Check(blockers.Length == 1 && blockers[0].IsEnabled, "One bundled blocker must be enabled on first launch");
        var extension = blockers[0];
        core.Navigate($"chrome-extension://{extension.Id}/dashboard.html");
        await WaitForAsync(() => core.Source.EndsWith("/dashboard.html"), "extension dashboard");
        await Task.Delay(300);
        await core.ExecuteScriptAsync("""
            window.testReady = false;
            chrome.runtime.sendMessage({ what: 'getOptionsPageData' }).then(async data => {
                window.testConfig = { mode: data.defaultFilteringMode, permission: data.hasOmnipotence,
                    lists: data.enabledRulesets,
                    scripts: (await chrome.scripting.getRegisteredContentScripts()).length };
                window.testReady = true;
            }).catch(e => window.testError = String(e));
            """);
        await WaitForScriptAsync(core, "window.testReady === true", "extension worker readiness");
        var configJson = await core.ExecuteScriptAsync("window.testConfig");
        using var config = JsonDocument.Parse(configJson);
        Check(config.RootElement.GetProperty("mode").GetInt32() >= 2, "YouTube needs optimal filtering and scriptlets");
        Check(config.RootElement.GetProperty("permission").GetBoolean(), "Extension must have host permissions");
        Check(config.RootElement.GetProperty("scripts").GetInt32() > 0, "Real content filtering scripts must register");
        Console.WriteLine($"Blocker configuration: {configJson}");

        var blockedByClient = false;
        var receiver = core.GetDevToolsProtocolEventReceiver("Network.loadingFailed");
        void Failed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
        {
            using var data = JsonDocument.Parse(args.ParameterObjectAsJson);
            if (data.RootElement.GetProperty("errorText").GetString() == "net::ERR_BLOCKED_BY_CLIENT") blockedByClient = true;
        }
        void Respond(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            var uri = new Uri(args.Request.Uri);
            if (uri.Host is not ("www.youtube.com" or "googleads.g.doubleclick.net" or "i.ytimg.com")) return;
            if (uri.Host != "www.youtube.com")
            {
                const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><rect width='20' height='20'/></svg>";
                args.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(svg)),
                    200, "OK", "Content-Type: image/svg+xml\r\nCache-Control: no-store");
                return;
            }
            const string html = """
                <!doctype html><title>Adblock fixture</title><body>
                <div id="contents"><ytd-rich-item-renderer id="ad"><ytd-ad-slot-renderer>Sponsored content</ytd-ad-slot-renderer></ytd-rich-item-renderer></div>
                <div id="content">Normal video content</div><video id="video" controls></video>
                <script>window.testFetch = url => {
                    window.fetchResult = 'pending';
                    const img = new Image(); window.probeImage = img;
                    img.onload = () => window.fetchResult='allowed';
                    img.onerror = () => window.fetchResult='blocked';
                    img.src = url;
                };</script>
                """;
            args.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)),
                200, "OK", "Content-Type: text/html\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: *");
        }
        core.AddWebResourceRequestedFilter("https://*/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += Respond;
        receiver.DevToolsProtocolEventReceived += Failed;
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
            core.Navigate("https://www.youtube.com/watch?v=viewer-fixture");
            await WaitForAsync(() => core.DocumentTitle == "Adblock fixture", "ad filtering fixture");
            await WaitForScriptAsync(core, "typeof window.testFetch === 'function'", "fixture script");
            await core.ExecuteScriptAsync("testFetch('https://googleads.g.doubleclick.net/pagead/ads?viewer=1')");
            await WaitForScriptAsync(core, "window.fetchResult === 'blocked'", "blocked advertising request");
            Check(blockedByClient, "Failure must be caused by a browser filter, not a network outage");
            await core.ExecuteScriptAsync("testFetch('https://i.ytimg.com/vi/example/hqdefault.jpg')");
            await WaitForScriptAsync(core, "window.fetchResult === 'allowed'", "allowed thumbnail request");
            await WaitForScriptAsync(core, "!document.getElementById('ad') || getComputedStyle(document.getElementById('ad')).display === 'none'",
                "cosmetic YouTube ad filtering");
            Check(await core.ExecuteScriptAsync("getComputedStyle(document.getElementById('content')).display !== 'none' && !!document.querySelector('video')") == "true",
                "Normal content and player must remain present");
            await extension.EnableAsync(false);
            await core.ExecuteScriptAsync("testFetch('https://googleads.g.doubleclick.net/pagead/ads?viewer=2')");
            await WaitForScriptAsync(core, "window.fetchResult === 'allowed'", "disabled blocking");

            // A fresh service simulates app startup against the same isolated profile.
            var serviceType = typeof(YouTubeViewer.MainWindow).Assembly.GetType("YouTubeViewer.AdBlocker")!;
            var second = Activator.CreateInstance(serviceType)!;
            await (Task)serviceType.GetMethod("InitializeAsync")!.Invoke(second, new object[] { core, parentWindow })!;
            Check(!(bool)serviceType.GetProperty("IsEnabled")!.GetValue(second)!, "Disabled state must survive reinstallation/startup");
            Check((await core.Profile.GetBrowserExtensionsAsync()).Count(item => item.Name == "uBlock Origin Lite") == 1,
                "Startup must not accumulate duplicate extensions");
            await (Task)serviceType.GetMethod("ToggleAsync")!.Invoke(second, null)!;
            await core.ExecuteScriptAsync("testFetch('https://googleads.g.doubleclick.net/pagead/ads?viewer=3')");
            await WaitForScriptAsync(core, "window.fetchResult === 'blocked'", "re-enabled blocking");
            Console.WriteLine("PASS real uBO Lite: network and cosmetic filtering, normal content, disable/enable, persisted state and stable identity");
        }
        finally
        {
            core.WebResourceRequested -= Respond;
            core.RemoveWebResourceRequestedFilter("https://*/*", CoreWebView2WebResourceContext.All);
            receiver.DevToolsProtocolEventReceived -= Failed;
        }
    }

    private static async Task WaitForScriptAsync(CoreWebView2 core, string script, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await core.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(5)) != "true")
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(description);
            await Task.Delay(100);
        }
    }
}
