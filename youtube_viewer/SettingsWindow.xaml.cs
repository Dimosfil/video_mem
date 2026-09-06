using System.Windows;

namespace YouTubeViewer;

public partial class SettingsWindow : Window
{
    public SettingsWindow(ConnectionSettings settings)
    {
        InitializeComponent();
        ProxyUrlBox.Text = settings.ProxyUrl;
        SystemRouteRadio.IsChecked = settings.Mode == RoutingMode.System;
        LocalProxyRadio.IsChecked = settings.Mode == RoutingMode.LocalProxy;
        UpdateProxyField();
    }

    public ConnectionSettings? SelectedSettings { get; private set; }

    private void RoutingMode_Checked(object sender, RoutedEventArgs e) => UpdateProxyField();

    private void UpdateProxyField()
    {
        if (ProxyUrlBox is not null)
        {
            ProxyUrlBox.IsEnabled = LocalProxyRadio?.IsChecked == true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var mode = LocalProxyRadio.IsChecked == true
                ? RoutingMode.LocalProxy
                : RoutingMode.System;
            var proxyUrl = mode == RoutingMode.LocalProxy
                ? ProxyConfiguration.Parse(ProxyUrlBox.Text).Address.AbsoluteUri.TrimEnd('/')
                : NormalizeOptionalProxy(ProxyUrlBox.Text);
            SelectedSettings = new ConnectionSettings(mode, proxyUrl);
            DialogResult = true;
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(
                exception.Message,
                "Настройки подключения",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ProxyUrlBox.Focus();
            ProxyUrlBox.SelectAll();
        }
    }

    private static string NormalizeOptionalProxy(string value)
    {
        try
        {
            return ProxyConfiguration.Parse(value).Address.AbsoluteUri.TrimEnd('/');
        }
        catch (InvalidOperationException)
        {
            return ProxyConfiguration.DefaultAddress;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
