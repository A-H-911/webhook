using FluentAssertions;
using FluentValidation;
using MediatR;
using NSubstitute;
using WebhookService.Application.Common.Behaviors;

namespace WebhookService.UnitTests.Application.Behaviors;

public sealed class ValidationBehaviorTests
{
    private sealed record TestRequest(string Value) : IRequest<string>;

    private sealed class AlwaysFailValidator : AbstractValidator<TestRequest>
    {
        public AlwaysFailValidator()
        {
            RuleFor(x => x.Value).NotEmpty().WithMessage("Value is required.");
        }
    }

    [Fact]
    public async Task Handle_CallsNext_WhenNoValidatorsRegistered()
    {
        var behavior = new ValidationBehavior<TestRequest, string>([]);
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next().Returns("ok");

        var result = await behavior.Handle(new TestRequest("x"), next, CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_CallsNext_WhenValidationPasses()
    {
        var validator = new AlwaysFailValidator();
        var behavior = new ValidationBehavior<TestRequest, string>([validator]);
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next().Returns("ok");

        var result = await behavior.Handle(new TestRequest("hello"), next, CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_ThrowsValidationException_WhenValidationFails()
    {
        var validator = new AlwaysFailValidator();
        var behavior = new ValidationBehavior<TestRequest, string>([validator]);
        var next = Substitute.For<RequestHandlerDelegate<string>>();

        var act = () => behavior.Handle(new TestRequest(string.Empty), next, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Value is required*");
        await next.DidNotReceive()();
    }
}