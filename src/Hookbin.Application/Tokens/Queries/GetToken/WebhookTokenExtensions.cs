using Hookbin.Domain.Entities;

namespace Hookbin.Application.Tokens.Queries.GetToken;

internal static class WebhookTokenExtensions
{
    internal static TokenDto ToDto(this WebhookToken token, string baseUrl) => new(
        token.Id,
        token.Token,
        token.Name,
        $"{baseUrl.TrimEnd('/')}/webhook/{token.Token}",
        token.Description,
        token.CreatedAt,
        token.IsActive,
        token.CustomResponse is null ? null : new CustomResponseDto(
            token.CustomResponse.StatusCode,
            token.CustomResponse.ContentType,
            token.CustomResponse.Body,
            token.CustomResponse.Headers));
}
