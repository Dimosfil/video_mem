using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using YouTubeViewer;

internal static class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main()
    {
        // Exercise real WPF resources and routed menu commands without opening
        // windows, navigating to YouTube, or accessing the user's web profile.
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            VerifyMoveAndBulkClose();
            VerifySingleTabMenu();
            VerifySessionAcrossWindows();
            Console.WriteLine("PASS WPF tab identity, selection, context menus, bulk close and restoration history");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            app.Shutdown();
        }
    }

    private static void VerifyMoveAndBulkClose()
    {
        var window = new MainWindow();
        try
        {
            var a = AddTab(window, "A");
            var b = AddTab(window, "B");
            var c = AddTab(window, "C");
            var d = AddTab(window, "D");
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = Item(b);
            var originalView = Item(b).Content;
            Invoke(window, "MoveTab", b, d, true);
            Check(ReferenceEquals(tabs.SelectedItem, Item(b)), "Moved active tab must remain selected");
            Check(ReferenceEquals(Item(b).Content, originalView), "Move must preserve the WebView instance");
            CheckOrder(window, "A,C,D,B");
            Invoke(window, "MoveTab", d, a, false);
            CheckOrder(window, "D,A,C,B");
            Check(ReferenceEquals(tabs.SelectedItem, Item(b)), "Moving another tab must preserve selection");

            var menu = Item(a).ContextMenu;
            Check(!ReferenceEquals(menu, Item(b).ContextMenu), "Each tab must have its own menu");
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            var right = Command(menu, TabCloseScope.Right);
            Check(right.IsEnabled, "Right group must be enabled");
            Check(ReferenceEquals(right.DataContext, a), "Background menu must target its own tab");
            right.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            CheckOrder(window, "D,A");
            Check(ReferenceEquals(tabs.SelectedItem, Item(a)), "Bulk close should select surviving anchor");
            var history = (ClosedTabHistory)Field(window, "_closedTabs");
            Check(history.Pop() == "https://example.test/B", "Last closed tab should restore first");
            Check(history.Pop() == "https://example.test/C", "Initializing tabs must retain their requested URL");

            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Check(!Command(menu, TabCloseScope.Right).IsEnabled, "Empty right group must be disabled");
            Command(menu, TabCloseScope.Left).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            CheckOrder(window, "A");
            Check(history.Count == 1, "Bulk close must record each closed tab once");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifySessionAcrossWindows()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"viewer-ui-session-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            var store = new BrowserSessionStore(System.IO.Path.Combine(directory, "session.json"));
            store.Save(new BrowserSession(new[] { "https://a.test/", "https://b.test/", "https://c.test/" }, 1));
            var window = new MainWindow(store);
            Invoke(window, "RestoreSessionTabs");
            var tabs = (IList)Field(window, "_tabs");
            var originalB = tabs[1]!;
            var originalC = tabs[2]!;
            Invoke(window, "MoveTab", originalC, tabs[0]!, false);
            Check(store.Load().SelectedIndex == 2, "Reorder must persist the selected tab's new position");
            Invoke(window, "RememberAddress", originalB, "https://b.test/watch?v=updated");
            window.Close(); // Real Closing handler must save before disposing uninitialized views.

            var nextWindow = new MainWindow(store);
            Invoke(nextWindow, "RestoreSessionTabs");
            var restored = (BrowserSession)typeof(MainWindow).GetMethod("CaptureSession", PrivateInstance)!.Invoke(nextWindow, null)!;
            Check(string.Join(",", restored.Addresses) == "https://c.test/,https://a.test/,https://b.test/watch?v=updated",
                "Restart must recover all URLs, including an updated address, in visual order");
            Check(restored.SelectedIndex == 2, "Restart must restore selected tab");
            var restoredTabs = (IList)Field(nextWindow, "_tabs");
            var anchor = restoredTabs[0]!;
            Command(Item(anchor).ContextMenu, TabCloseScope.Right).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(store.Load().Addresses.Single() == "https://c.test/", "Explicit bulk close must persist survivors");
            nextWindow.Close();

            var finalWindow = new MainWindow(store);
            Invoke(finalWindow, "RestoreSessionTabs");
            var remaining = ((IList)Field(finalWindow, "_tabs"))[0]!;
            Command(Item(remaining).ContextMenu, TabCloseScope.All).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(store.Load().Addresses.Length == 0, "Explicit Close All must not resurrect intentionally closed tabs");
        }
        finally
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    private static void VerifySingleTabMenu()
    {
        var window = new MainWindow();
        var tab = AddTab(window, "only");
        var menu = Item(tab).ContextMenu;
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        foreach (var scope in new[] { TabCloseScope.Left, TabCloseScope.Right, TabCloseScope.Others })
        {
            Check(!Command(menu, scope).IsEnabled, "Single-tab bulk command should be disabled");
        }
        Check(!menu.Items.OfType<MenuItem>().Last().IsEnabled, "Empty reopen history should be disabled");
        var closed = false;
        window.Closed += (_, _) => closed = true;
        Command(menu, TabCloseScope.All).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(closed, "Close All must close the last window");
        Check(((IList)Field(window, "_tabs")).Count == 0, "Close All must clear tab ownership");
    }

    private static object AddTab(MainWindow window, string title)
    {
        var type = typeof(MainWindow).GetNestedType("BrowserTab", BindingFlags.NonPublic)!;
        var tab = Activator.CreateInstance(type)!;
        var view = new WebView2();
        var label = new TextBlock { Text = title };
        var item = new TabItem { Header = label, Content = view };
        type.GetProperty("Item")!.SetValue(tab, item);
        type.GetProperty("Title")!.SetValue(tab, label);
        type.GetProperty("View")!.SetValue(tab, view);
        type.GetProperty("InitialAddress")!.SetValue(tab, $"https://example.test/{title}");
        ((IList)Field(window, "_tabs")).Add(tab);
        ((TabControl)window.FindName("Tabs")).Items.Add(item);
        Invoke(window, "ConfigureTabInteractions", tab);
        return tab;
    }

    private static void CheckOrder(MainWindow window, string expected)
    {
        var model = ((IList)Field(window, "_tabs")).Cast<object>().Select(Item).ToArray();
        var displayed = ((TabControl)window.FindName("Tabs")).Items.Cast<TabItem>().ToArray();
        Check(model.SequenceEqual(displayed), "Model and visual order must match");
        Check(string.Join(",", displayed.Select(tab => ((TextBlock)tab.Header).Text)) == expected,
            $"Expected tab order {expected}");
    }

    private static TabItem Item(object tab) => (TabItem)tab.GetType().GetProperty("Item")!.GetValue(tab)!;
    private static object Field(MainWindow window, string name) =>
        typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(window)!;
    private static void Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(window, args);
    private static MenuItem Command(ContextMenu menu, TabCloseScope scope) =>
        menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, scope));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
