using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using WebhookService.Domain.Services;

namespace WebhookService.Infrastructure.Sse;

internal sealed class SseNotifier : ISseNotifier
{
    private readonly ConcurrentDictionary<Guid, List<Channel<SseEvent>>> _subscribers = new();
    private readonly Lock _lock = new();

    public async IAsyncEnumerable<SseEvent> SubscribeAsync(
        Guid tokenId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<SseEvent>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

        lock (_lock)
        {
            _subscribers.GetOrAdd(tokenId, _ => []).Add(channel);
        }

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                yield return evt;
        }
        finally
        {
            lock (_lock)
            {
                if (_subscribers.TryGetValue(tokenId, out var list))
                {
                    list.Remove(channel);
                    if (list.Count == 0)
                        _subscribers.TryRemove(tokenId, out _);
                }
            }
        }
    }

    public async Task NotifyAsync(Guid tokenId, string summaryJson, CancellationToken ct = default)
    {
        var evt = new SseEvent("request", summaryJson);
        List<Channel<SseEvent>> snapshot;

        lock (_lock)
        {
            if (!_subscribers.TryGetValue(tokenId, out var list))
                return;
            snapshot = [.. list];
        }

        foreach (var channel in snapshot)
            await channel.Writer.WriteAsync(evt, ct);
    }

    public void NotifyTokenDeleted(Guid tokenId)
    {
        List<Channel<SseEvent>> snapshot;

        lock (_lock)
        {
            if (!_subscribers.TryGetValue(tokenId, out var list))
                return;
            snapshot = [.. list];
            _subscribers.TryRemove(tokenId, out _);
        }

        foreach (var channel in snapshot)
            channel.Writer.TryComplete();
    }
}
