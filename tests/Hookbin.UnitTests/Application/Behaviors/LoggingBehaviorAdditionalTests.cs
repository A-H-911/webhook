using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Hookbin.Application.Common.Behaviors;

namespace Hookbin.UnitTests.Application.Behaviors;

// Must be top-level public so NSubstitute/Castle can create
// ILogger<LoggingBehavior<LoggingAdditionalTestRequest, string>> proxies
// (same constraint as LoggingTestRequest in LoggingBehaviorTests.cs)
public sealed record LoggingAdditionalTestRequest(string Value) : IRequest<string>;

public sealed class LoggingBehaviorAdditionalTests
{

    [Fact]
    public async Task Handle_RethrowsValidationException_WhenNextThrowsValidation()
    {
        // Arrange
        var logger = Substitute.For<ILogger<LoggingBehavior<LoggingAdditionalTestRequest, string>>>();
        var behavior = new LoggingBehavior<LoggingAdditionalTestRequest, string>(logger);
        var failures = new[] { new ValidationFailure("Value", "Value is required") };
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next().Returns<string>(_ => throw new ValidationException(failures));

        // Act
        var act = () => behavior.Handle(new LoggingAdditionalTestRequest("x"), next, CancellationToken.None);

        // Assert — ValidationException is re-thrown, not swallowed
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_LogsWarning_WhenValidationExceptionThrown()
    {
        // Arrange
        var logger = Substitute.For<ILogger<LoggingBehavior<LoggingAdditionalTestRequest, string>>>();
        var behavior = new LoggingBehavior<LoggingAdditionalTestRequest, string>(logger);
        var failures = new[] { new ValidationFailure("Value", "Value is required") };
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next().Returns<string>(_ => throw new ValidationException(failures));

        // Act — consume the exception so the assertion below runs
        try { await behavior.Handle(new LoggingAdditionalTestRequest("x"), next, CancellationToken.None); }
        catch (ValidationException) { }

        // Assert — LogWarning (not LogError) is used for expected validation failures
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_DoesNotLogError_WhenValidationExceptionThrown()
    {
        // Arrange
        var logger = Substitute.For<ILogger<LoggingBehavior<LoggingAdditionalTestRequest, string>>>();
        var behavior = new LoggingBehavior<LoggingAdditionalTestRequest, string>(logger);
        var failures = new[] { new ValidationFailure("Value", "bad") };
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next().Returns<string>(_ => throw new ValidationException(failures));

        // Act
        try { await behavior.Handle(new LoggingAdditionalTestRequest("x"), next, CancellationToken.None); }
        catch (ValidationException) { }

        // Assert — LogError must NOT fire for an expected validation failure
        logger.DidNotReceive().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_LogsInformation_OnSuccess()
    {
        // Arrange
        var logger = Substitute.For<ILogger<LoggingBehavior<LoggingAdditionalTestRequest, string>>>();
        var behavior = new LoggingBehavior<LoggingAdditionalTestRequest, string>(logger);
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next().Returns("ok");

        // Act
        await behavior.Handle(new LoggingAdditionalTestRequest("x"), next, CancellationToken.None);

        // Assert
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_LogsError_OnUnhandledException()
    {
        // Arrange
        var logger = Substitute.For<ILogger<LoggingBehavior<LoggingAdditionalTestRequest, string>>>();
        var behavior = new LoggingBehavior<LoggingAdditionalTestRequest, string>(logger);
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next().Returns<string>(_ => throw new InvalidOperationException("db down"));

        // Act
        try { await behavior.Handle(new LoggingAdditionalTestRequest("x"), next, CancellationToken.None); }
        catch (InvalidOperationException) { }

        // Assert — LogError fires for unexpected exceptions
        logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<InvalidOperationException>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
