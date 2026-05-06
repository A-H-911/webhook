using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebhookService.Domain.Entities;

namespace WebhookService.Infrastructure.Persistence.Configurations;

public sealed class WebhookRequestConfiguration : IEntityTypeConfiguration<WebhookRequest>
{
    public void Configure(EntityTypeBuilder<WebhookRequest> builder)
    {
        builder.ToTable("WebhookRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Method).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Path).HasMaxLength(2048).IsRequired();
        builder.Property(r => r.QueryString).HasMaxLength(4000); // SQL Server nvarchar max fixed length is 4000
        builder.Property(r => r.ContentType).HasMaxLength(256);
        builder.Property(r => r.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(r => r.UserAgent).HasMaxLength(512);
        builder.Property(r => r.ReceivedAt).IsRequired().HasColumnType("datetimeoffset(7)");

        builder.HasIndex(r => r.TokenId);
        builder.HasIndex(r => r.ReceivedAt);

        builder.HasOne(r => r.Token)
               .WithMany()
               .HasForeignKey(r => r.TokenId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
