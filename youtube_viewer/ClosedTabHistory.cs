namespace YouTubeViewer;

public sealed class ClosedTabHistory
{
    private readonly LinkedList<string> _addresses = new();
    private readonly int _capacity;

    public ClosedTabHistory(int capacity = 20)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public int Count => _addresses.Count;

    public void Push(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        _addresses.AddFirst(address);
        while (_addresses.Count > _capacity)
        {
            _addresses.RemoveLast();
        }
    }

    public string? Pop()
    {
        if (_addresses.First is null)
        {
            return null;
        }

        var address = _addresses.First.Value;
        _addresses.RemoveFirst();
        return address;
    }
}
