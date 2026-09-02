using YouTubeViewer;

var tests = new (string Name, Action Run)[]
{
    ("empty address opens YouTube", () =>
        Equal(BrowserAddress.Home, BrowserAddress.Resolve("  "))),
    ("absolute HTTPS address is preserved", () =>
        Equal("https://www.youtube.com/watch?v=test", BrowserAddress.Resolve("https://www.youtube.com/watch?v=test"))),
    ("host-like address receives HTTPS", () =>
        Equal("https://youtu.be/test", BrowserAddress.Resolve("youtu.be/test"))),
    ("plain text becomes a YouTube search", () =>
        Equal("https://www.youtube.com/results?search_query=lofi%20music", BrowserAddress.Resolve("lofi music"))),
    ("default Happ proxy is selected", () =>
        Equal(ProxyConfiguration.DefaultAddress + "/", ProxyConfiguration.Load(_ => null).Address.AbsoluteUri)),
    ("proxy override is selected", () =>
        Equal("http://127.0.0.1:18080/", ProxyConfiguration.Load(_ => "http://127.0.0.1:18080").Address.AbsoluteUri)),
    ("WebView2 proxy arguments disable QUIC", () =>
        Equal("--proxy-server=http://127.0.0.1:10809 --disable-quic", ProxyConfiguration.Load(_ => null).BrowserArguments)),
    ("invalid proxy is rejected", () =>
        Throws<InvalidOperationException>(() => ProxyConfiguration.Load(_ => "not-a-proxy"))),
    ("profile root does not duplicate WebView2 directory", () =>
        Equal(
            Path.Combine("C:\\Users\\Test\\AppData\\Local", "VideoMem", "YouTubeViewer"),
            ViewerProfile.UserDataFolder("C:\\Users\\Test\\AppData\\Local"))),
    ("closed tabs reopen in LIFO order", () =>
    {
        var history = new ClosedTabHistory();
        history.Push("first");
        history.Push("second");
        Equal("second", history.Pop());
        Equal("first", history.Pop());
    }),
    ("closed tab history is bounded", () =>
    {
        var history = new ClosedTabHistory(2);
        history.Push("first");
        history.Push("second");
        history.Push("third");
        Equal(2, history.Count);
        Equal("third", history.Pop());
        Equal("second", history.Pop());
    }),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
