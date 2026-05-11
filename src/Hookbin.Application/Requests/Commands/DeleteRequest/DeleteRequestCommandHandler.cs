using MediatR;
using Hookbin.Domain.Repositories;

namespace Hookbin.Application.Requests.Commands.DeleteRequest;

internal sealed class DeleteRequestCommandHandler(IWebhookRequestRepository repository)
    : IRequestHandler<DeleteRequestCommand, bool>
{
    public async Task<bool> Handle(DeleteRequestCommand request, CancellationToken cancellationToken)
    {
        var existing = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (existing is null || existing.TokenId != request.TokenId)
            return false;

        await repository.DeleteAsync(request.Id, cancellationToken);
        return true;
    }
}
