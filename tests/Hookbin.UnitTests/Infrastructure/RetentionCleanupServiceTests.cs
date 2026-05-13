using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Hookbin.Application.Options;
using Hookbin.Domain.Repositories;
using Hookbin.Infrastructure.BackgroundServices;

namespace Hookbin.UnitTests.Infrastructure;

public sealed class RetentionCleanupServiceTests
{
    private static IOptions<WebhookOptions> MakeOptions(int retentionDays) =>
        Options.Create(new WebhookOptions
        {
            BaseUrl = "https://example.com",
            RetentionDays = retentionDays,
            MaxRequestSizeMb = 5
        });

    /// <summary>
    /// Builds a real DI scope factory that resolves the given repository substitute.
    /// Using a real ServiceProvider avoids mocking the complex AsyncServiceScope struct.
    /// </summary>
    private static IServiceScopeFactory MakeScopeFactory(IWebhookRequestRepository repo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task RunCleanupAsync_DeletesOldRequests_WhenRetentionDaysIsPositive()
    {
        // Arrange
        var repo = Substitute.For<IWebhookRequestRepository>();
        // Use a TCS so we don't rely on wall-clock timing. BackgroundService schedules
        // ExecuteAsync on a thread pool thread; we must wait for the actual call rather
        // than assuming it runs before StopAsync is reached.
        var tcs = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.DeleteOlderThanAsync(
                Arg.Do<DateTimeOffset>(d => tcs.TrySetResult(d)),
                Arg.Any<CancellationToken>())
            .Returns(3);
        var logger = Substitute.For<ILogger<RetentionCleanupService>>();
        var service = new RetentionCleanupService(MakeScopeFactory(repo), MakeOptions(7), logger);

        // Act
        await service.StartAsync(CancellationToken.None);
        var cutoff = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        // Assert — repository was called with a cutoff in the past
        cutoff.Should().BeBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task RunCleanupAsync_SkipsDelete_WhenRetentionDaysIsZero()
    {
        // Arrange
        var repo = Substitute.For<IWebhookRequestRepository>();
        var logger = Substitute.For<ILogger<RetentionCleanupService>>();
        var service = new RetentionCleanupService(MakeScopeFactory(repo), MakeOptions(0), logger);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(250);

        // Assert — zero means disabled; repository must not be called
        await repo.DidNotReceive().DeleteOlderThanAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCleanupAsync_SkipsDelete_WhenRetentionDaysIsNegative()
    {
        // Arrange
        var repo = Substitute.For<IWebhookRequestRepository>();
        var logger = Substitute.For<ILogger<RetentionCleanupService>>();
        var service = new RetentionCleanupService(MakeScopeFactory(repo), MakeOptions(-1), logger);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(250);

        // Assert
        await repo.DidNotReceive().DeleteOlderThanAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCleanupAsync_DoesNotThrow_WhenRepositoryThrows()
    {
        // Arrange — simulate a transient DB failure
        var repo = Substitute.For<IWebhookRequestRepository>();
        repo.DeleteOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB timeout"));
        var logger = Substitute.For<ILogger<RetentionCleanupService>>();
        var service = new RetentionCleanupService(MakeScopeFactory(repo), MakeOptions(7), logger);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        // Act — service must catch the exception and not crash the host
        var act = async () =>
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(250);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunCleanupAsync_LogsError_WhenRepositoryThrows()
    {
        // Arrange
        var repo = Substitute.For<IWebhookRequestRepository>();
        repo.DeleteOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("connection refused"));
        var logger = Substitute.For<ILogger<RetentionCleanupService>>();
        var service = new RetentionCleanupService(MakeScopeFactory(repo), MakeOptions(7), logger);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(250);

        // Assert — the caught exception must be recorded at Error level
        logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task RunCleanupAsync_PassesCutoffApproximately_RetentionDaysBeforeNow()
    {
        // Arrange
        var repo = Substitute.For<IWebhookRequestRepository>();
        DateTimeOffset? capturedCutoff = null;
        repo.DeleteOlderThanAsync(
                Arg.Do<DateTimeOffset>(d => capturedCutoff = d),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var logger = Substitute.For<ILogger<RetentionCleanupService>>();
        var before = DateTimeOffset.UtcNow;
        var service = new RetentionCleanupService(MakeScopeFactory(repo), MakeOptions(30), logger);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(250);

        // Assert — cutoff is approximately (now - 30 days)
        capturedCutoff.Should().NotBeNull();
        var expectedCutoff = before.AddDays(-30);
        capturedCutoff!.Value.Should().BeCloseTo(expectedCutoff, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunCleanupAsync_LogsInformation_AfterSuccessfulDelete()
    {
        // Arrange
        var repo = Substitute.For<IWebhookRequestRepository>();
        repo.DeleteOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(5);
        var logger = Substitute.For<ILogger<RetentionCleanupService>>();
        var service = new RetentionCleanupService(MakeScopeFactory(repo), MakeOptions(7), logger);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(250);

        // Assert — success path logs at Information level
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
