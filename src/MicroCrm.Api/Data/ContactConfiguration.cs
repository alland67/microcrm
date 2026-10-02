using MicroCrm.Api.Features.Contacts;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroCrm.Api.Data;

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.Property(c => c.FirstName).UseCollation("NOCASE");
        builder.Property(c => c.LastName).UseCollation("NOCASE");
        builder.Property(c => c.Email).UseCollation("NOCASE");
        builder.HasIndex(c => c.Email).IsUnique();
    }
}
