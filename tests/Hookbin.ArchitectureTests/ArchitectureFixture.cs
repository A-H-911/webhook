using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using SystemAssembly = System.Reflection.Assembly;
using Hookbin.API.Controllers;
using Hookbin.Application.Tokens.Commands.CreateToken;
using Hookbin.Domain.Entities;
using Hookbin.Infrastructure.Persistence;

namespace Hookbin.ArchitectureTests;

public sealed class ArchitectureFixture
{
    public Architecture Architecture { get; }

    public ArchitectureFixture()
    {
        var baseDir = Path.GetDirectoryName(typeof(ArchitectureFixture).Assembly.Location)!;
        var streamWorkerAsm = SystemAssembly.LoadFrom(Path.Combine(baseDir, "Hookbin.StreamWorker.dll"));
        var jobsWorkerAsm = SystemAssembly.LoadFrom(Path.Combine(baseDir, "Hookbin.JobsWorker.dll"));

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
