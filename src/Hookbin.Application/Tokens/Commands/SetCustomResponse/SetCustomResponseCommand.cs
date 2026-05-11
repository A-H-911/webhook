using MediatR;

namespace Hookbin.Application.Tokens.Commands.SetCustomResponse;

public sealed record SetCustomResponseCommand(
    Guid TokenId,
    int StatusCode,
    string ContentType,
    string? Body,
    string Headers) : IRequest<bool>;
