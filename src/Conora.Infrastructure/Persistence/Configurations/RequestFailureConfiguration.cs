using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conora.Infrastructure.Persistence.Configurations;

public class RequestFailureConfiguration : IEntityTypeConfiguration<RequestFailure>
{
    public void Configure(EntityTypeBuilder<RequestFailure> builder)
    {
        builder.ToTable("request_failures");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.StatusCode).IsRequired();
        builder.Property(e => e.Title).IsRequired().HasMaxLength(80);
        builder.Property(e => e.Area).IsRequired().HasMaxLength(40);
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.Version).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
        builder.Property(e => e.DeletedAt);

        builder.HasIndex(e => e.OccurredAt).HasDatabaseName("IX_RequestFailures_OccurredAt");
        builder.HasIndex(e => new { e.Area, e.StatusCode }).HasDatabaseName("IX_RequestFailures_Area_Status");
    }
}
