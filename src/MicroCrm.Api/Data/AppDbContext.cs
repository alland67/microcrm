using MicroCrm.Api.Features.Contacts;

using Microsoft.EntityFrameworkCore;

namespace MicroCrm.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new ContactConfiguration());
}
