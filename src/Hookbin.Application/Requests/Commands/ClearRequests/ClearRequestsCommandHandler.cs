using MediatR;
using Hookbin.Domain.Repositories;

namespace Hookbin.Application.Requests.Commands.ClearRequests;

internal sealed class ClearRequestsCommandHandler(IWebhookRequestRepository repository)
    : IRequestHandler<ClearRequestsCommand>
{
    public async Task Handle(ClearRequestsCommand request, CancellationToken cancellationToken)
        => await repository.DeleteAllForTokenAsync(request.TokenId, cancellationToken);
}
