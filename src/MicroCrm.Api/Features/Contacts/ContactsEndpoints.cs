using MicroCrm.Api.Data;

using Microsoft.AspNetCore.Http.HttpResults;

namespace MicroCrm.Api.Features.Contacts;

public static class ContactsEndpoints
{
    public static IEndpointRouteBuilder MapContactsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/contacts");

        group.MapPost(string.Empty, CreateContact);

        return app;
    }

    private static async Task<Created<ContactResponse>> CreateContact(
        CreateContactRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var contact = new Contact
        {
            Id = Guid.CreateVersion7(now),
            FirstName = request.FirstName ?? string.Empty,
            LastName = request.LastName,
            Email = request.Email?.Trim(),
            Phone = request.Phone,
            Company = request.Company,
            Notes = request.Notes,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/contacts/{contact.Id}", ContactResponse.From(contact));
    }
}
