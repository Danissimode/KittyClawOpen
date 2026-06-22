namespace KittyClaw.Web.Services;

using System.Threading.Channels;

public sealed class BoardUpdateNotifier
{
    public event Action<string>? OnProjectUpdated;

    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public void NotifyProjectUpdated(string slug)
    {
        OnProjectUpdated?.Invoke(slug);
        _channel.Writer.TryWrite(slug);
    }

    public IAsyncEnumerable<string> UpdatesAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
