using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using YouTubeViewer;

internal static partial class Program
{
    private static void RunBrowserTests(bool verifyYouTube = false, string? playbackUrl = null, bool disableQuic = false, string? proxyUrl = null, bool adBlockOnly = false)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var frame = new DispatcherFrame();
        var dispatcher = Application.Current.Dispatcher;
        var task = VerifyBrowserRuntimeAsync(verifyYouTube, playbackUrl, disableQuic, proxyUrl, adBlockOnly);
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(
            new Action(() => frame.Continue = false)), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        SynchronizationContext.SetSynchronizationContext(previousContext);
        task.GetAwaiter().GetResult();
    }

    private static async Task VerifyBrowserRuntimeAsync(bool verifyYouTube, string? playbackUrl, bool disableQuic, string? proxyUrl, bool adBlockOnly)
    {
        var directory = Path.GetFullPath(Path.Combine("downloads", $"viewer-runtime-test-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(directory);
        var store = new BrowserSessionStore(Path.Combine(directory, "session.json"));
        store.Save(new BrowserSession(Enumerable.Range(0, 7).Select(i => $"http://viewer.test/tab{i}").ToArray(), 3));
        var window = new MainWindow(store)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000,
            Top = -30000,
        };
        // Run the production tab path with a private environment and in-memory
        // responses; never load the user's settings or profile. Only the
        // explicit --youtube mode navigates to a real public YouTube URL.
        window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window,
            typeof(MainWindow).GetMethod("Window_Loaded", PrivateInstance)!);
        try
        {
            var browserArguments = !verifyYouTube ? "--no-proxy-server"
                : proxyUrl is not null ? ProxyConfiguration.Parse(proxyUrl).BrowserArguments
                : disableQuic ? "--disable-quic" : "";
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(directory, "profile"),
                options: new CoreWebView2EnvironmentOptions(browserArguments) { AreBrowserExtensionsEnabled = true })
                .WaitAsync(TimeSpan.FromSeconds(20));
            Console.WriteLine("Live WebView2 environment initialized");
            typeof(MainWindow).GetField("_webViewEnvironment", PrivateInstance)!.SetValue(window, environment);
            ((FrameworkElement)window.FindName("StartupPanel")).Visibility = Visibility.Collapsed;
            window.Show();
            Invoke(window, "RestoreSessionTabs");
            var tabs = ((IList)Field(window, "_tabs")).Cast<object>().ToArray();
            var views = tabs.Select(tab => (WebView2)tab.GetType().GetProperty("View")!.GetValue(tab)!).ToArray();
            var deferred = new List<(CoreWebView2WebResourceRequestedEventArgs Request, CoreWebView2Deferral Deferral)>();
            var delayedOnce = false;
            foreach (var view in views)
            {
                view.CoreWebView2InitializationCompleted += (_, e) =>
                {
                    if (!e.IsSuccess) return;
                    var core = view.CoreWebView2;
                    core.AddWebResourceRequestedFilter("http://viewer.test/*", CoreWebView2WebResourceContext.All);
                    core.WebResourceRequested += (_, request) =>
                    {
                        var path = new Uri(request.Request.Uri).AbsolutePath;
                        if (path == "/pending-image" || (path == "/slow" && !delayedOnce))
                        {
                            if (path == "/slow") delayedOnce = true;
                            deferred.Add((request, request.GetDeferral()));
                            return;
                        }
                        var html = $"<!doctype html><title>{path}</title><body style='background:green'>Loaded {path}</body>";
                        if (path == "/slow-resource") html += "<img src='/pending-image'>";
                        request.Response = environment.CreateWebResourceResponse(
                            new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK", "Content-Type: text/html; charset=utf-8");
                    };
                };
            }
            var selectedInitialization = (Task)typeof(MainWindow)
                .GetMethod("InitializeSelectedTabAsync", PrivateInstance)!.Invoke(window, null)!;
            await selectedInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitForAsync(() => views[3].CoreWebView2?.DocumentTitle == "/tab3", "selected restored page");
            Check(views.Where((_, i) => i != 3).All(view => view.CoreWebView2 is null),
                "Startup must finish without initializing hidden tabs");
            Console.WriteLine("PASS selected page loads without waiting for six hidden tabs");
            Check(((Button)window.FindName("AdBlockButton")).IsEnabled,
                $"Bundled blocker must initialize: {((Button)window.FindName("AdBlockButton")).ToolTip}");
            if (adBlockOnly)
            {
                await VerifyAdBlockerAsync(views[3].CoreWebView2, new System.Windows.Interop.WindowInteropHelper(window).Handle);
                return;
            }
            if (verifyYouTube)
            {
                var core = views[3].CoreWebView2;
                if (playbackUrl is not null)
                {
                    await VerifyPlaybackAsync(core, tabs[3], playbackUrl);
                    return;
                }
                core.Navigate("https://www.youtube.com/results?search_query=linkin+park+in+moscow");
                await WaitForAsync(() => core.DocumentTitle.Contains("YouTube", StringComparison.OrdinalIgnoreCase),
                    "live YouTube page title", 35);
                var until = DateTime.UtcNow.AddSeconds(20);
                while (await core.ExecuteScriptAsync("document.querySelector('ytd-search') !== null") != "true")
                {
                    if (DateTime.UtcNow > until) throw new TimeoutException("YouTube search content did not render");
                    await Task.Delay(200);
                }
                var capture = Path.Combine(directory, "youtube-search.png");
                using (var output = File.Create(capture))
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                Console.WriteLine($"PASS live YouTube search title and rendered search component; preview: {capture}");
                return;
            }
            var control = (TabControl)window.FindName("Tabs");
            for (var i = 0; i < tabs.Length; i++)
            {
                control.SelectedItem = Item(tabs[i]);
                await WaitForAsync(() => views[i].CoreWebView2?.DocumentTitle == $"/tab{i}", $"restored page {i}");
                await WaitForAsync(() => views[i].IsVisible && views[i].ActualHeight > 100,
                    $"visible viewport for tab {i}");
                Check((await views[i].CoreWebView2.ExecuteScriptAsync("document.body.innerText")).Contains($"Loaded /tab{i}"),
                    "Selected WebView must render its own document");
            }
            var selectedView = views[6];
            await selectedView.CoreWebView2.ExecuteScriptAsync("window.testMarker = 'kept'");
            Invoke(window, "MoveTab", tabs[6], tabs[0], false);
            Check((await selectedView.CoreWebView2.ExecuteScriptAsync("window.testMarker")) == "\"kept\"",
                "Moving a live tab must preserve its document state");
            Check(ReferenceEquals(control.SelectedItem, Item(tabs[6])), "Live move must preserve selection");

            control.SelectedItem = Item(tabs[0]);
            views[0].CoreWebView2.Navigate("http://viewer.test/slow-resource");
            await WaitForAsync(() => views[0].CoreWebView2.DocumentTitle == "/slow-resource",
                "document with a pending image");
            var resourceNavigation = tabs[0].GetType().GetProperty("NavigationId")!.GetValue(tabs[0]);
            await WaitForAsync(() => deferred.Count > 0, "pending image request");
            // The document is usable, but NavigationCompleted must still be waiting for the image.
            Check(await views[0].CoreWebView2.ExecuteScriptAsync("document.readyState") == "\"interactive\"",
                "Delayed image must keep the document between DOM readiness and window.load");

            control.SelectedItem = Item(tabs[6]);
            selectedView.CoreWebView2.Navigate("http://viewer.test/slow");
            await WaitForAsync(() => delayedOnce, "delayed navigation request");
            control.SelectedItem = Item(tabs[1]);
            await WaitForAsync(() => ((FrameworkElement)window.FindName("LoadingBar")).Visibility == Visibility.Collapsed,
                "completed tab must not inherit the other tab's loading indicator");
            Console.WriteLine("Checking the real 30-second navigation deadline");
            await WaitForAsync(() => tabs[6].GetType().GetProperty("LoadError")!.GetValue(tabs[6]) is not null,
                "navigation timeout", 35);
            Check(tabs[0].GetType().GetProperty("LoadError")!.GetValue(tabs[0]) is null,
                "A usable document must not time out because an image is still loading");
            Check(Equals(resourceNavigation, tabs[0].GetType().GetProperty("NavigationId")!.GetValue(tabs[0])),
                "The ready document must not be replaced by an automatic retry");
            Check(((FrameworkElement)window.FindName("BrowserErrorPanel")).Visibility == Visibility.Collapsed,
                "A background failure must not cover the current page");
            control.SelectedItem = Item(tabs[6]);
            Check(((FrameworkElement)window.FindName("BrowserErrorPanel")).Visibility == Visibility.Visible,
                "Timed-out tab must show a retry panel instead of a blank view");
            Check(selectedView.Visibility == Visibility.Hidden, "Native WebView must not cover the WPF error panel");
            var retry = (Task)typeof(MainWindow).GetMethod("ReloadCurrentTabAsync", PrivateInstance)!.Invoke(window, null)!;
            await retry;
            await WaitForAsync(() => selectedView.CoreWebView2.DocumentTitle == "/slow", "retry after timeout");
            Check(((FrameworkElement)window.FindName("BrowserErrorPanel")).Visibility == Visibility.Collapsed,
                "A successful retry must clear the error panel");
            foreach (var pending in deferred) pending.Deferral.Complete();
            Console.WriteLine("PASS live navigation timeout, background isolation, retry and live tab movement");
            Console.WriteLine("PASS live WebView2: seven restored pages initialize, switch and retain their documents");
            await VerifyTabCreationAsync(window, environment, store);
            control.SelectedItem = Item(tabs[0]);
            await VerifyThumbnailRecoveryAsync(views[0].CoreWebView2, environment);
            await VerifyAdBlockerAsync(views[0].CoreWebView2, new System.Windows.Interop.WindowInteropHelper(window).Handle);
        }
        finally
        {
            window.Close();
            // Chromium may release profile handles asynchronously. Keep the
            // isolated ignored evidence directory instead of forcing deletion.
            Console.WriteLine($"Runtime evidence: {directory}");
        }
    }

    private static async Task VerifyPlaybackAsync(CoreWebView2 core, object tab, string address)
    {
        var uri = new Uri(address);
        Check(uri.Scheme == "https" && uri.Host == "www.youtube.com" && uri.AbsolutePath == "/watch",
            "Playback smoke requires a public https://www.youtube.com/watch URL");
        core.NavigationCompleted += (_, args) => Console.WriteLine($"Navigation: {args.IsSuccess}, {args.WebErrorStatus}");
        var documentReady = false;
        core.DOMContentLoaded += (_, _) =>
        {
            documentReady = true;
            Console.WriteLine("Playback document DOM ready");
        };
        var hosts = new Dictionary<string, string>();
        var networkMessages = 0;
        core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived += (_, e) =>
        {
            using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            var root = json.RootElement;
            if (Uri.TryCreate(root.GetProperty("request").GetProperty("url").GetString(), UriKind.Absolute, out var requestUri))
                hosts[root.GetProperty("requestId").GetString()!] = requestUri.Host;
        };
        core.GetDevToolsProtocolEventReceiver("Network.loadingFailed").DevToolsProtocolEventReceived += (_, e) =>
        {
            using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            var root = json.RootElement;
            var host = hosts.GetValueOrDefault(root.GetProperty("requestId").GetString()!, "unknown");
            if (networkMessages++ < 15) Console.WriteLine($"Network failure: {host}: {root.GetProperty("errorText").GetString()}");
        };
        core.GetDevToolsProtocolEventReceiver("Network.responseReceived").DevToolsProtocolEventReceived += (_, e) =>
        {
            using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            var response = json.RootElement.GetProperty("response");
            var responseUri = new Uri(response.GetProperty("url").GetString()!);
            var status = response.GetProperty("status").GetInt32();
            if ((status >= 400 || responseUri.Host.EndsWith(".googlevideo.com")) && networkMessages++ < 15)
                Console.WriteLine($"Network response: {responseUri.Host}: {status}, {response.GetProperty("protocol").GetString()}");
        };
        await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
        await core.AddScriptToExecuteOnDocumentCreatedAsync("document.addEventListener('play', e => { if (e.target instanceof HTMLMediaElement) e.target.muted = true; }, true);");
        core.Navigate(address);
        var started = DateTime.UtcNow;
        var deadline = started.AddSeconds(75);
        double previousTime = -1;
        var progressingSamples = 0;
        var sample = "null";
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000);
            if (tab.GetType().GetProperty("LoadError")!.GetValue(tab) is string loadError)
                throw new InvalidOperationException($"Playback interrupted by application: {loadError}; state: {sample}");
            if (!documentReady) continue;
            sample = await core.ExecuteScriptAsync("""
                (() => {
                  const v = document.querySelector('video');
                  const p = document.querySelector('#movie_player');
                  if (!v) return { video: false, ready: document.readyState };
                  v.muted = true;
                  if (v.paused) v.play().catch(() => {});
                  return { video: true, time: v.currentTime, ready: v.readyState,
                    frames: v.getVideoPlaybackQuality().totalVideoFrames,
                    paused: v.paused, error: v.error?.code ?? null,
                    ad: p?.classList.contains('ad-showing') ?? false,
                    playability: p?.getPlayerResponse?.()?.playabilityStatus?.status ?? null };
                })()
                """).WaitAsync(TimeSpan.FromSeconds(10));
            using var document = JsonDocument.Parse(sample);
            var state = document.RootElement;
            if (state.GetProperty("video").GetBoolean())
            {
                var current = state.GetProperty("time").GetDouble();
                if (!state.GetProperty("ad").GetBoolean() && !state.GetProperty("paused").GetBoolean() &&
                    state.GetProperty("frames").GetInt32() > 0 && current > previousTime + 0.2)
                    progressingSamples++;
                else progressingSamples = 0;
                previousTime = current;
                if (progressingSamples >= 5 && DateTime.UtcNow - started > TimeSpan.FromSeconds(35))
                {
                    Check(tab.GetType().GetProperty("LoadError")!.GetValue(tab) is null,
                        "Playing video must remain visible beyond the navigation deadline");
                    Console.WriteLine($"PASS live video playback beyond 35 seconds: {sample}");
                    return;
                }
            }
            if (tab.GetType().GetProperty("LoadError")!.GetValue(tab) is string error)
                throw new InvalidOperationException($"Playback interrupted by application: {error}; state: {sample}");
        }
        throw new TimeoutException($"Video did not sustain playback: {sample}");
    }

    private static async Task WaitForAsync(Func<bool> predicate, string description, int seconds = 15)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!predicate())
        {
            if (DateTime.UtcNow >= until) throw new TimeoutException($"Timed out waiting for {description}");
            await Task.Delay(50);
        }
    }
}
