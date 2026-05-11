using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using SystemAssembly = System.Reflection.Assembly;
using WebhookService.API.Controllers;
using WebhookService.Application.Tokens.Commands.CreateToken;
using WebhookService.Domain.Entities;
using WebhookService.Infrastructure.Persistence;

namespace WebhookService.ArchitectureTests;

public sealed class ArchitectureFixture
{
    public Architecture Architecture { get; }

    public ArchitectureFixture()
    {
        var baseDir = Path.GetDirectoryName(typeof(ArchitectureFixture).Assembly.Location)!;
        var streamWorkerAsm = SystemAssembly.LoadFrom(Path.Combine(baseDir, "WebhookService.StreamWorker.dll"));
        var jobsWorkerAsm = SystemAssembly.LoadFrom(Path.Combine(baseDir, "WebhookService.JobsWorker.dll"));

        Architecture = new ArchLoader()
            .LoadAssemblies(
                typeof(WebhookToken).Assembly,              // Domain
                typeof(CreateTokenCommand).Assembly,        // Application
                typeof(ApplicationDbContext).Assembly,      // Infrastructure
                typeof(TokensController).Assembly           // API
            )
            .LoadAssemblies(streamWorkerAsm, jobsWorkerAsm)
            .Build();
    }
}
