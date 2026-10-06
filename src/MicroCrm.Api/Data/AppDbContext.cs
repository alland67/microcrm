using MicroCrm.Api.Features.Contacts;
using MicroCrm.Api.Features.Todos;

using Microsoft.EntityFrameworkCore;

namespace MicroCrm.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ContactConfiguration());
        modelBuilder.ApplyConfiguration(new TodoConfiguration());
    }
}
