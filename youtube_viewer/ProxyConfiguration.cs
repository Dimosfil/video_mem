using System.Net.Sockets;

namespace YouTubeViewer;

public sealed record ProxyConfiguration(Uri Address)
{
    public const string EnvironmentVariable = "YOUTUBE_VIEWER_PROXY";
    public const string DefaultAddress = "http://127.0.0.1:10809";

    public string BrowserArguments => $"--proxy-server={Address.AbsoluteUri.TrimEnd('/')} --disable-quic";

    public static ProxyConfiguration Load(Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        var configured = readEnvironment(EnvironmentVariable);
        var value = string.IsNullOrWhiteSpace(configured) ? DefaultAddress : configured.Trim();

        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(address.Host) ||
            address.Port <= 0)
        {
            throw new InvalidOperationException(
                $"Некорректный адрес прокси в {EnvironmentVariable}: {value}");
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
