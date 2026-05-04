using Microsoft.EntityFrameworkCore;
using WebhookService.Domain.Entities;
using WebhookService.Infrastructure.Persistence.Configurations;

namespace WebhookService.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<WebhookToken> WebhookTokens => Set<WebhookToken>();
    public DbSet<WebhookRequest> WebhookRequests => Set<WebhookRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WebhookTokenConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookRequestConfiguration());
    }
}
