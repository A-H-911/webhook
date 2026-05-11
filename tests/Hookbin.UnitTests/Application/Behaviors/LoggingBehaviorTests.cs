using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Hookbin.Application.Common.Behaviors;

namespace Hookbin.UnitTests.Application.Behaviors;

// Must be top-level public so NSubstitute can create ILogger<LoggingBehavior<LoggingTestRequest, string>> proxy
public sealed record LoggingTestRequest(string Value) : IRequest<string>;

public sealed class LoggingBehaviorTests
{
    [Fact]
    public async Task Handle_ReturnsResponse_OnSuccess()
    {
        var logger = NullLogger<LoggingBehavior<LoggingTestRequest, string>>.Instance;
        var behavior = new LoggingBehavior<LoggingTestRequest, string>(logger);
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next().Returns("result");

        var result = await behavior.Handle(new LoggingTestRequest("x"), next, CancellationToken.None);

        result.Should().Be("result");
    }

    [Fact]
    public async Task Handle_RethrowsException_WhenNextThrows()
    {
        var logger = NullLogger<LoggingBehavior<LoggingTestRequest, string>>.Instance;
        var behavior = new LoggingBehavior<LoggingTestRequest, string>(logger);
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next().Returns<string>(_ => throw new InvalidOperationException("boom"));

        var act = () => behavior.Handle(new LoggingTestRequest("x"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}