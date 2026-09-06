using System.Net.Sockets;

namespace YouTubeViewer;

public sealed record ProxyConfiguration(Uri Address)
{
    public const string DefaultAddress = "http://127.0.0.1:10809";

    public string BrowserArguments => $"--proxy-server={Address.AbsoluteUri.TrimEnd('/')} --disable-quic";

    public static ProxyConfiguration Parse(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(address.Host) ||
            address.Port <= 0)
        {
            throw new InvalidOperationException($"Некорректный адрес локального HTTP-прокси: {value}");
        }

        return new ProxyConfiguration(address);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(Address.Host, Address.Port, timeout.Token);
            return true;
        }
        catch (Exception exception) when (
            exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
