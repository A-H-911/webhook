using Hookbin.Domain.Entities;

namespace Hookbin.Application.Caching;

public interface ITokenCache
{
    Task<WebhookToken?> GetAsync(Guid tokenGuid, CancellationToken ct = default);
    Task SetAsync(Guid tokenGuid, WebhookToken token, CancellationToken ct = default);
    Task RemoveAsync(Guid tokenGuid, CancellationToken ct = default);
}
