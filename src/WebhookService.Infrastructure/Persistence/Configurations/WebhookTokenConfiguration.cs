using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebhookService.Domain.Entities;

namespace WebhookService.Infrastructure.Persistence.Configurations;

public sealed class WebhookTokenConfiguration : IEntityTypeConfiguration<WebhookToken>
{
    public void Configure(EntityTypeBuilder<WebhookToken> builder)
    {
        builder.ToTable("WebhookTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Token).IsRequired();
        builder.HasIndex(t => t.Token).IsUnique();

        builder.Property(t => t.Description).HasMaxLength(200);
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.IsActive).IsRequired();

        builder.OwnsOne(t => t.CustomResponse, cr =>
        {
            cr.Property(c => c.StatusCode).HasColumnName("CustomResponse_StatusCode");
            cr.Property(c => c.ContentType).HasMaxLength(256).HasColumnName("CustomResponse_ContentType");
            cr.Property(c => c.Body).HasColumnName("CustomResponse_Body");
            cr.Property(c => c.Headers).HasColumnName("CustomResponse_Headers");
        });
    }
}
