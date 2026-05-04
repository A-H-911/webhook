using MediatR;

namespace WebhookService.Application.Tokens.Commands.ResetCustomResponse;

public sealed record ResetCustomResponseCommand(Guid Id) : IRequest<bool>;
