using MicroCrm.Api.Features.Contacts;
using MicroCrm.Api.Features.Todos;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroCrm.Api.Data;

public sealed class TodoConfiguration : IEntityTypeConfiguration<Todo>
{
    public void Configure(EntityTypeBuilder<Todo> builder)
    {
        builder.Property(t => t.CreatedAt).HasConversion(
            v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        builder.Property(t => t.UpdatedAt).HasConversion(
            v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        builder.Property(t => t.CompletedAt).HasConversion(
            v => v.HasValue ? v.Value.UtcTicks : (long?)null,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

        builder.HasOne<Contact>()
            .WithMany()
            .HasForeignKey(t => t.ContactId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.IsDone, t.DueDate }).HasDatabaseName("IX_Todos_IsDone_DueDate");

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Todos_DoneState",
            "(\"IsDone\" = 0 AND \"CompletedAt\" IS NULL) OR (\"IsDone\" = 1 AND \"CompletedAt\" IS NOT NULL)"));
    }
}
