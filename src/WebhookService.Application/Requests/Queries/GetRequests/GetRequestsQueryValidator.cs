using FluentValidation;

namespace WebhookService.Application.Requests.Queries.GetRequests;

public sealed class GetRequestsQueryValidator : AbstractValidator<GetRequestsQuery>
{
    public GetRequestsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search)
            .MaximumLength(200)
            .When(x => x.Search is not null);
    }
}
