using System.Collections;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using YouTubeViewer;

internal static partial class Program
{
    private static async Task VerifyTabCreationAsync(MainWindow window, CoreWebView2Environment environment,
        BrowserSessionStore store)
    {
        var control = (TabControl)window.FindName("Tabs");
        var model = (IList)Field(window, "_tabs");
        Invoke(window, "SetChromeEnabled", true);
        NotifyCollectionChangedEventHandler interceptNewViews = (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (TabItem item in e.NewItems)
            {
                var view = (WebView2)item.Content;
                view.CoreWebView2InitializationCompleted += (_, args) =>
                {
                    if (!args.IsSuccess) return;
                    view.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    view.CoreWebView2.WebResourceRequested += (_, request) =>
                        request.Response = environment.CreateWebResourceResponse(
                            new MemoryStream(Encoding.UTF8.GetBytes("<html><title>Created tab</title><body>Local test</body></html>")),
                            200, "OK", "Content-Type: text/html; charset=utf-8");
                };
            }
        };
        ((INotifyCollectionChanged)control.Items).CollectionChanged += interceptNewViews;
        try
        {
            var originalCount = model.Count;
            var source = model[1]!;
            var originalView = (WebView2)Item(source).Content;
            await originalView.CoreWebView2.ExecuteScriptAsync("window.duplicateMarker = 'original'");
            const string address = "http://viewer.test/current?value=changed#fragment";
            Invoke(window, "RememberAddress", source, address);
            control.SelectedIndex = 0;
            var menu = Item(source).ContextMenu;
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            var duplicate = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "Duplicate"));
            Check(duplicate.IsEnabled, "Duplication must not depend on closed-tab history");
            duplicate.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(model.Count == originalCount + 1, "Menu must create exactly one duplicate");
            var copy = model[2]!;
            Check(ReferenceEquals(control.SelectedItem, Item(copy)), "Background duplicate must open selected to its source's right");
            Check(!ReferenceEquals(Item(copy).Content, originalView), "Duplicate must own an independent WebView");
            var copyView = (WebView2)Item(copy).Content;
            await WaitForAsync(() => copyView.CoreWebView2?.DocumentTitle == "Created tab", "duplicate page navigation");
            Check(copyView.Source.AbsoluteUri == address, "Duplicate must use the current URL, including query and fragment");
            Check(store.Load().Addresses[2] == address && store.Load().SelectedIndex == 2,
                "Duplicate order, URL and selection must persist");
            Check(await originalView.CoreWebView2.ExecuteScriptAsync("window.duplicateMarker") == "\"original\"",
                "Duplicating must preserve the original document");

            control.ApplyTemplate();
            var plus = (Button)control.Template.FindName("NewTabButton", control);
            window.Width = window.MinWidth;
            window.UpdateLayout();
            var position = plus.TranslatePoint(new Point(), control);
            Check(plus.IsEnabled && plus.ActualWidth > 0 && position.X >= 0 &&
                  position.X + plus.ActualWidth <= control.ActualWidth + 1,
                "Plus must remain inside the tab strip when many tabs overflow at minimum width");
            plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(model.Count == originalCount + 2, "Plus must create exactly one real tab");
            var newTab = model[model.Count - 1]!;
            var newView = (WebView2)Item(newTab).Content;
            await WaitForAsync(() => newView.CoreWebView2?.DocumentTitle == "Created tab", "new home page navigation");
            Check(newView.Source.AbsoluteUri == BrowserAddress.Home, "Plus must open the home page");
            Check(ReferenceEquals(control.SelectedItem, Item(newTab)), "New tab must be selected");
            Check(control.Items.Cast<TabItem>().SequenceEqual(model.Cast<object>().Select(Item)),
                "Plus must not enter the real tab order");
            Console.WriteLine("PASS tab-strip plus, overflow, background duplication, independent document and persisted order");
        }
        finally
        {
            ((INotifyCollectionChanged)control.Items).CollectionChanged -= interceptNewViews;
        }
    }
}
