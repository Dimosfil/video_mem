using YouTubeViewer;

var tests = new (string Name, Action Run)[]
{
    ("only one session owner can write, and ownership is released at exit", () =>
    {
        var name = $"Local\\YouTubeViewer.Tests.{Guid.NewGuid():N}";
        using (var first = new ViewerInstance(name))
        {
            Equal(true, first.TryAcquire());
            Equal(true, first.TryAcquire());
            Equal(false, Task.Run(() =>
            {
                using var second = new ViewerInstance(name);
                return second.TryAcquire();
            }).GetAwaiter().GetResult());
        }
        Equal(true, Task.Run(() =>
        {
            using var next = new ViewerInstance(name);
            return next.TryAcquire();
        }).GetAwaiter().GetResult());
    }),
    ("session survives restart with Unicode URLs, duplicates, order and selection", () =>
    {
        WithSessionStore((store, _) =>
        {
            Equal(0, store.Load().Addresses.Length);
            var addresses = new[] { "https://www.youtube.com/watch?v=first", "https://example.test/видео", "https://www.youtube.com/watch?v=first" };
            store.Save(new BrowserSession(addresses, 1));
            var restored = store.Load();
            Equal(string.Join("|", addresses), string.Join("|", restored.Addresses));
            Equal(1, restored.SelectedIndex);
            store.Save(BrowserSession.Empty);
            Equal(0, store.Load().Addresses.Length);
        });
    }),
    ("session validates saved addresses and remaps selected tab", () =>
    {
        var normalized = new BrowserSession(new[] { "https://a.test", "javascript:alert(1)", "https://b.test" }, 2).Normalize();
        Equal("https://a.test|https://b.test", string.Join("|", normalized.Addresses));
        Equal(1, normalized.SelectedIndex);
        Equal(0, new BrowserSession(new[] { "https://a.test" }, -10).Normalize().SelectedIndex);
        Equal(0, new BrowserSession(null!, 99).Normalize().SelectedIndex);
    }),
    ("damaged session falls back to intact backup without overwriting it", () =>
    {
        WithSessionStore((store, path) =>
        {
            store.Save(new BrowserSession(new[] { "https://a.test" }, 0));
            store.Save(new BrowserSession(new[] { "https://b.test" }, 0));
            File.WriteAllText(path, "{broken");
            Equal("https://a.test", store.Load().Addresses.Single());
            store.Save(new BrowserSession(new[] { "https://c.test" }, 0));
            File.WriteAllText(path, "{broken again");
            Equal("https://a.test", store.Load().Addresses.Single());
            Equal(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
        });
    }),
    ("bulk close follows the clicked tab and current visual order", () =>
    {
        var tabs = new List<string> { "A", "B", "C", "D" };
        Equal("A", string.Join(",", TabOperations.SelectToClose(tabs, "B", TabCloseScope.Left)));
        Equal("C,D", string.Join(",", TabOperations.SelectToClose(tabs, "B", TabCloseScope.Right)));
        Equal("A,C,D", string.Join(",", TabOperations.SelectToClose(tabs, "B", TabCloseScope.Others)));
        Equal("B", string.Join(",", TabOperations.SelectToClose(tabs, "B", TabCloseScope.Current)));
        Equal("A,B,C,D", string.Join(",", TabOperations.SelectToClose(tabs, "B", TabCloseScope.All)));
        tabs.Remove("D");
        tabs.Insert(0, "D");
        Equal("D,A", string.Join(",", TabOperations.SelectToClose(tabs, "B", TabCloseScope.Left)));
    }),
    ("empty sides, single tab and stale menu anchors are safe", () =>
    {
        var tabs = new[] { "A" };
        foreach (var scope in new[] { TabCloseScope.Left, TabCloseScope.Right, TabCloseScope.Others })
        {
            Equal(0, TabOperations.SelectToClose(tabs, "A", scope).Length);
        }
        Equal(1, TabOperations.SelectToClose(tabs, "A", TabCloseScope.All).Length);
        Equal(0, TabOperations.SelectToClose(tabs, "missing", TabCloseScope.All).Length);
    }),
    ("bulk close snapshot survives removals and records restoration order", () =>
    {
        var tabs = new List<string> { "A", "B", "C", "D" };
        var history = new ClosedTabHistory();
        foreach (var tab in TabOperations.SelectToClose(tabs, "A", TabCloseScope.Right))
        {
            history.Push(tab);
            tabs.Remove(tab);
        }
        Equal("A", string.Join(",", tabs));
        Equal("D", history.Pop());
        Equal("C", history.Pop());
        Equal("B", history.Pop());
    }),
    ("drop before or after a tab preserves identity and ordering in both directions", () =>
    {
        foreach (var example in new[]
        {
            (Source: 0, Target: 2, After: false, Expected: "B,A,C,D"),
            (Source: 0, Target: 3, After: true, Expected: "B,C,D,A"),
            (Source: 3, Target: 0, After: false, Expected: "D,A,B,C"),
            (Source: 3, Target: 1, After: true, Expected: "A,B,D,C"),
            (Source: 1, Target: 2, After: false, Expected: "A,B,C,D"),
            (Source: 2, Target: 1, After: true, Expected: "A,B,C,D"),
        })
        {
            var tabs = new List<string> { "A", "B", "C", "D" };
            var moved = tabs[example.Source];
            var destination = TabOperations.MoveDestination(example.Source, example.Target, example.After);
            tabs.RemoveAt(example.Source);
            tabs.Insert(destination, moved);
            Equal(example.Expected, string.Join(",", tabs));
        }
    }),
    ("empty address opens YouTube", () =>
        Equal(BrowserAddress.Home, BrowserAddress.Resolve("  "))),
    ("absolute HTTPS address is preserved", () =>
        Equal("https://www.youtube.com/watch?v=test", BrowserAddress.Resolve("https://www.youtube.com/watch?v=test"))),
    ("host-like address receives HTTPS", () =>
        Equal("https://youtu.be/test", BrowserAddress.Resolve("youtu.be/test"))),
    ("plain text becomes a YouTube search", () =>
        Equal("https://www.youtube.com/results?search_query=lofi%20music", BrowserAddress.Resolve("lofi music"))),
    ("system routing is the default", () =>
    {
        Equal(RoutingMode.System, ConnectionSettings.Default.Mode);
        Equal<ProxyConfiguration?>(null, ConnectionSettings.Default.GetProxy());
    }),
    ("custom local proxy is selected", () =>
        Equal(
            "http://127.0.0.1:18080/",
            new ConnectionSettings(RoutingMode.LocalProxy, "http://127.0.0.1:18080")
                .GetProxy()!.Address.AbsoluteUri)),
    ("local proxy WebView2 arguments disable QUIC", () =>
        Equal(
            "--proxy-server=http://127.0.0.1:10809 --disable-quic",
            new ConnectionSettings(RoutingMode.LocalProxy, ProxyConfiguration.DefaultAddress)
                .GetProxy()!.BrowserArguments)),
    ("invalid local proxy is rejected", () =>
        Throws<InvalidOperationException>(() =>
            new ConnectionSettings(RoutingMode.LocalProxy, "not-a-proxy").GetProxy())),
    ("connection settings persist outside the build", () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), $"youtube-viewer-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ConnectionSettingsStore(Path.Combine(directory, "settings.json"));
            Equal(RoutingMode.System, store.Load().Mode);

            var expected = new ConnectionSettings(RoutingMode.LocalProxy, "http://127.0.0.1:18080");
            store.Save(expected);
            Equal(expected, store.Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }),
    ("invalid settings fall back to system routing", () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), $"youtube-viewer-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{invalid json");
            Equal(RoutingMode.System, new ConnectionSettingsStore(path).Load().Mode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }),
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

static void WithSessionStore(Action<BrowserSessionStore, string> test)
{
    var directory = Path.Combine(Path.GetTempPath(), $"youtube-viewer-session-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "session.json");
        test(new BrowserSessionStore(path), path);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
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
