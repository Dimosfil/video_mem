using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace YouTubeViewer;

public partial class MainWindow : Window
{
    private sealed class BrowserTab
    {
        public required TabItem Item { get; init; }
        public required TextBlock Title { get; init; }
        public required WebView2 View { get; init; }
    }

    private readonly List<BrowserTab> _tabs = new();
    private readonly ClosedTabHistory _closedTabs = new();
    private CoreWebView2Environment? _webViewEnvironment;
    private bool _initialized;
    private bool _fullScreen;
    private WindowState _windowStateBeforeFullScreen;
    private WindowStyle _windowStyleBeforeFullScreen;

    public MainWindow()
    {
        InitializeComponent();
    }

    private BrowserTab? CurrentTab =>
        Tabs.SelectedItem is TabItem item
            ? _tabs.FirstOrDefault(tab => ReferenceEquals(tab.Item, item))
            : null;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var proxy = ProxyConfiguration.Load();
            StartupText.Text = $"Проверка Happ: {proxy.Address.Host}:{proxy.Address.Port}…";

            if (!await proxy.IsAvailableAsync())
            {
                MessageBox.Show(
                    $"Локальный прокси Happ недоступен: {proxy.Address}\n\n" +
                    "Запустите Happ, подключитесь к VPN и затем откройте YouTube Viewer снова.\n" +
                    "Прямое подключение отключено, чтобы трафик YouTube не обходил VPN.",
                    "YouTube Viewer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
                return;
            }

            var profileDirectory = ViewerProfile.UserDataFolder();

            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = proxy.BrowserArguments,
            };
            _webViewEnvironment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: profileDirectory,
                options);

            _initialized = true;
            StartupPanel.Visibility = Visibility.Collapsed;
            SetChromeEnabled(true);
            await CreateTabAsync(BrowserAddress.Home);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Не удалось запустить просмотрщик:\n\n{exception.Message}",
                "YouTube Viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private async Task CreateTabAsync(string address, bool focusAddress = false)
    {
        if (_webViewEnvironment is null)
        {
            return;
        }

        var view = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(15, 15, 15),
        };
        var title = new TextBlock
        {
            Text = "Новая вкладка",
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 190,
        };
        var closeButton = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(0),
            FontSize = 15,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ToolTip = "Закрыть вкладку (Ctrl+W)",
            Cursor = Cursors.Hand,
        };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };
        header.Children.Add(title);
        header.Children.Add(closeButton);

        var item = new TabItem
        {
            Header = header,
            Content = view,
        };
        var browserTab = new BrowserTab
        {
            Item = item,
            Title = title,
            View = view,
        };

        closeButton.Click += (_, _) => CloseTab(browserTab);
        _tabs.Add(browserTab);
        Tabs.Items.Add(item);
        Tabs.SelectedItem = item;

        await view.EnsureCoreWebView2Async(_webViewEnvironment);
        ConfigureWebView(browserTab);
        view.CoreWebView2.Navigate(BrowserAddress.Resolve(address));

        if (focusAddress)
        {
            AddressBox.Focus();
            AddressBox.SelectAll();
        }
    }

    private void ConfigureWebView(BrowserTab tab)
    {
        var core = tab.View.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;

        core.NavigationStarting += (_, args) =>
        {
            if (ReferenceEquals(CurrentTab, tab))
            {
                AddressBox.Text = args.Uri;
                LoadingBar.Visibility = Visibility.Visible;
                ShowStatus("Загрузка…");
            }
        };
        core.NavigationCompleted += (_, args) =>
        {
            if (ReferenceEquals(CurrentTab, tab))
            {
                LoadingBar.Visibility = Visibility.Collapsed;
                ShowStatus(args.IsSuccess ? string.Empty : $"Ошибка загрузки: {args.WebErrorStatus}");
                UpdateChrome();
            }
        };
        core.HistoryChanged += (_, _) => UpdateChromeIfCurrent(tab);
        core.SourceChanged += (_, _) => UpdateChromeIfCurrent(tab);
        core.DocumentTitleChanged += (_, _) =>
        {
            tab.Title.Text = string.IsNullOrWhiteSpace(core.DocumentTitle)
                ? "YouTube"
                : core.DocumentTitle;
            if (ReferenceEquals(CurrentTab, tab))
            {
                Title = $"{tab.Title.Text} — YouTube Viewer";
            }
        };
        core.NewWindowRequested += async (_, args) =>
        {
            args.Handled = true;
            await CreateTabAsync(args.Uri);
        };
        core.DownloadStarting += (_, args) =>
        {
            args.Cancel = true;
            ShowStatus("Скачивание отключено в просмотрщике.");
        };
        core.ContainsFullScreenElementChanged += (_, _) =>
        {
            SetFullScreen(core.ContainsFullScreenElement);
        };
    }

    private void UpdateChromeIfCurrent(BrowserTab tab)
    {
        if (ReferenceEquals(CurrentTab, tab))
        {
            UpdateChrome();
        }
    }

    private void UpdateChrome()
    {
        var tab = CurrentTab;
        var core = tab?.View.CoreWebView2;
        BackButton.IsEnabled = core?.CanGoBack == true;
        ForwardButton.IsEnabled = core?.CanGoForward == true;
        ReloadButton.IsEnabled = core is not null;
        HomeButton.IsEnabled = core is not null;
        GoButton.IsEnabled = core is not null;
        AddressBox.IsEnabled = core is not null;

        if (core is not null)
        {
            AddressBox.Text = core.Source;
            Title = string.IsNullOrWhiteSpace(core.DocumentTitle)
                ? "YouTube Viewer"
                : $"{core.DocumentTitle} — YouTube Viewer";
        }
    }

    private void SetChromeEnabled(bool enabled)
    {
        NewTabButton.IsEnabled = enabled;
        HomeButton.IsEnabled = enabled;
        AddressBox.IsEnabled = enabled;
        GoButton.IsEnabled = enabled;
    }

    private void NavigateFromAddressBar()
    {
        var core = CurrentTab?.View.CoreWebView2;
        if (core is null)
        {
            return;
        }

        core.Navigate(BrowserAddress.Resolve(AddressBox.Text));
        CurrentTab?.View.Focus();
    }

    private void CloseTab(BrowserTab tab)
    {
        if (!_tabs.Contains(tab))
        {
            return;
        }

        if (tab.View.CoreWebView2 is not null)
        {
            _closedTabs.Push(tab.View.CoreWebView2.Source);
        }

        _tabs.Remove(tab);
        Tabs.Items.Remove(tab.Item);
        tab.View.Dispose();

        if (_tabs.Count == 0)
        {
            Close();
        }
        else
        {
            UpdateChrome();
        }
    }

    private async Task ReopenClosedTabAsync()
    {
        var address = _closedTabs.Pop();
        if (address is not null)
        {
            await CreateTabAsync(address);
        }
    }

    private void CycleTabs(int offset)
    {
        if (Tabs.Items.Count < 2)
        {
            return;
        }

        var current = Math.Max(Tabs.SelectedIndex, 0);
        Tabs.SelectedIndex = (current + offset + Tabs.Items.Count) % Tabs.Items.Count;
    }

    private async Task HardReloadAsync()
    {
        var core = CurrentTab?.View.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            await core.CallDevToolsProtocolMethodAsync("Network.clearBrowserCache", "{}");
        }
        finally
        {
            core.Reload();
        }
    }

    private void SetFullScreen(bool enabled)
    {
        if (enabled == _fullScreen)
        {
            return;
        }

        if (enabled)
        {
            _windowStateBeforeFullScreen = WindowState;
            _windowStyleBeforeFullScreen = WindowStyle;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            BrowserChrome.Visibility = Visibility.Collapsed;
            LoadingBar.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = Visibility.Collapsed;
            SetTabStripVisibility(Visibility.Collapsed);
        }
        else
        {
            BrowserChrome.Visibility = Visibility.Visible;
            SetTabStripVisibility(Visibility.Visible);
            WindowStyle = _windowStyleBeforeFullScreen;
            WindowState = _windowStateBeforeFullScreen;
        }

        _fullScreen = enabled;
    }

    private void SetTabStripVisibility(Visibility visibility)
    {
        if (FindVisualChild<TabPanel>(Tabs) is { } tabPanel)
        {
            tabPanel.Visibility = visibility;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (child is not Viewport3DVisual && FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusPanel.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab?.View.CoreWebView2?.CanGoBack == true)
        {
            CurrentTab.View.CoreWebView2.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab?.View.CoreWebView2?.CanGoForward == true)
        {
            CurrentTab.View.CoreWebView2.GoForward();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) =>
        CurrentTab?.View.CoreWebView2?.Reload();

    private void HomeButton_Click(object sender, RoutedEventArgs e) =>
        CurrentTab?.View.CoreWebView2?.Navigate(BrowserAddress.Home);

    private void GoButton_Click(object sender, RoutedEventArgs e) => NavigateFromAddressBar();

    private async void NewTabButton_Click(object sender, RoutedEventArgs e) =>
        await CreateTabAsync(BrowserAddress.Home, focusAddress: true);

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateFromAddressBar();
            e.Handled = true;
        }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs)
        {
            UpdateChrome();
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.F11)
        {
            SetFullScreen(!_fullScreen);
        }
        else if (key == Key.F5 || (control && key == Key.R))
        {
            if (shift)
            {
                await HardReloadAsync();
            }
            else
            {
                CurrentTab?.View.CoreWebView2?.Reload();
            }
        }
        else if (key == Key.Escape)
        {
            CurrentTab?.View.CoreWebView2?.Stop();
        }
        else if (alt && key == Key.Left)
        {
            BackButton_Click(sender, e);
        }
        else if (alt && key == Key.Right)
        {
            ForwardButton_Click(sender, e);
        }
        else if (control && shift && key == Key.T)
        {
            await ReopenClosedTabAsync();
        }
        else if (control && key == Key.T)
        {
            await CreateTabAsync(BrowserAddress.Home, focusAddress: true);
        }
        else if (control && key == Key.W)
        {
            if (CurrentTab is { } tab)
            {
                CloseTab(tab);
            }
        }
        else if (control && key == Key.Tab)
        {
            CycleTabs(shift ? -1 : 1);
        }
        else if (control && key == Key.L)
        {
            AddressBox.Focus();
            AddressBox.SelectAll();
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        foreach (var tab in _tabs.ToArray())
        {
            tab.View.Dispose();
        }

        _tabs.Clear();
    }
}
