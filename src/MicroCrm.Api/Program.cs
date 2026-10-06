using MicroCrm.Api.Data;
using MicroCrm.Api.Features.Contacts;
using MicroCrm.Api.Features.Todos;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlite(new SqliteConnectionStringBuilder(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("MicroCrm"))
    {
        ForeignKeys = true,
    }.ToString()));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapContactsEndpoints();
app.MapTodosEndpoints();

app.Run();

public partial class Program;
