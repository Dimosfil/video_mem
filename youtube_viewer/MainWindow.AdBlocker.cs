using System.Windows;

namespace YouTubeViewer;

public partial class MainWindow
{
    private readonly AdBlocker _adBlocker = new();

    private void UpdateAdBlockButton()
    {
        AdBlockButton.IsEnabled = _adBlocker.IsAvailable;
        AdBlockButton.Content = !_adBlocker.IsAvailable ? "Блокировка: !" :
            _adBlocker.IsEnabled ? "Блокировка: вкл" : "Блокировка: выкл";
        AdBlockButton.ToolTip = !_adBlocker.IsAvailable
            ? $"Блокировка рекламы недоступна: {_adBlocker.Error ?? "подготовка"}"
            : _adBlocker.IsEnabled
                ? "uBlock Origin Lite включён. Нажмите, чтобы отключить и обновить текущую страницу."
                : "Блокировка рекламы отключена. Нажмите, чтобы включить и обновить текущую страницу.";
    }

    private async void AdBlockButton_Click(object sender, RoutedEventArgs e)
    {
        AdBlockButton.IsEnabled = false;
        try
        {
            await _adBlocker.ToggleAsync();
            if (!_windowClosing) await ReloadCurrentTabAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Не удалось переключить блокировку рекламы:\n{exception.Message}",
                "YouTube Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (!_windowClosing) UpdateAdBlockButton();
        }
    }
}
