using FluentAssertions;
using WebhookService.API.Services;

namespace WebhookService.UnitTests.API.Services;

public sealed class SessionRevocationStoreTests
{
    [Fact]
    public void IsRevoked_ReturnsFalse_ForUnknownSession()
    {
        var store = new SessionRevocationStore();

        store.IsRevoked("unknown-sid").Should().BeFalse();
    }

    [Fact]
    public void IsRevoked_ReturnsTrue_AfterRevoke()
    {
        var store = new SessionRevocationStore();

        store.Revoke("sid-1");

        store.IsRevoked("sid-1").Should().BeTrue();
    }

    [Fact]
    public void Revoke_IsIdempotent()
    {
        var store = new SessionRevocationStore();

        store.Revoke("sid-1");
        store.Revoke("sid-1");

        store.IsRevoked("sid-1").Should().BeTrue();
    }

    [Fact]
    public void Revoke_MultipleSessionsAreTrackedIndependently()
    {
        var store = new SessionRevocationStore();

        store.Revoke("sid-a");
        store.Revoke("sid-b");

        store.IsRevoked("sid-a").Should().BeTrue();
        store.IsRevoked("sid-b").Should().BeTrue();
        store.IsRevoked("sid-c").Should().BeFalse();
    }

    [Fact]
    public void PruneOlderThan_RemovesExpiredEntries()
    {
        var store = new SessionRevocationStore();
        store.Revoke("old-sid");

        // Zero max-age: the cutoff is UtcNow, so the entry (revoked moments ago) is pruned
        store.PruneOlderThan(TimeSpan.FromMilliseconds(0));

        store.IsRevoked("old-sid").Should().BeFalse();
    }

    [Fact]
    public void PruneOlderThan_KeepsEntriesNewerThanMaxAge()
    {
        var store = new SessionRevocationStore();
        store.Revoke("fresh-sid");

        store.PruneOlderThan(TimeSpan.FromHours(24));

        store.IsRevoked("fresh-sid").Should().BeTrue();
    }

    [Fact]
    public void PruneOlderThan_OnEmptyStore_DoesNotThrow()
    {
        var store = new SessionRevocationStore();

        var act = () => store.PruneOlderThan(TimeSpan.FromMinutes(5));

        act.Should().NotThrow();
    }

    [Fact]
    public void PruneOlderThan_DoesNotRemoveSessions_RevokedWithinMaxAge()
    {
        var store = new SessionRevocationStore();
        store.Revoke("sid-a");
        store.Revoke("sid-b");

        // Both sessions were revoked moments ago — a 24-hour window keeps them alive
        store.PruneOlderThan(TimeSpan.FromHours(24));

        store.IsRevoked("sid-a").Should().BeTrue();
        store.IsRevoked("sid-b").Should().BeTrue();
    }
}
