using System.Collections.Concurrent;

namespace Hookbin.API.Services;

public sealed class SessionRevocationStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new();

    public void Revoke(string sessionId) => _revoked[sessionId] = DateTimeOffset.UtcNow;

    public bool IsRevoked(string sessionId) => _revoked.ContainsKey(sessionId);

    public void PruneOlderThan(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        foreach (var key in _revoked.Where(p => p.Value < cutoff).Select(p => p.Key).ToList())
            _revoked.TryRemove(key, out _);
    }
}
