using MicroCrm.Api.Common;
using MicroCrm.Api.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MicroCrm.Api.Features.Contacts;

public static class ContactsEndpoints
{
    public static IEndpointRouteBuilder MapContactsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/contacts");

        group.MapPost(string.Empty, CreateContact)
            .WithName("CreateContact")
            .WithSummary("Create a contact")
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet(string.Empty, ListContacts)
            .WithName("ListContacts")
            .WithSummary("List contacts with paging and optional search");
        group.MapGet("/{id:guid}", GetContact)
            .WithName("GetContactById")
            .WithSummary("Get a contact by id");
        group.MapPut("/{id:guid}", UpdateContact)
            .WithName("UpdateContact")
            .WithSummary("Replace a contact")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/{id:guid}", DeleteContact)
            .WithName("DeleteContact")
            .WithSummary("Delete a contact")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<PagedResponse<ContactResponse>>, ValidationProblem>> ListContacts(
        AppDbContext db,
        CancellationToken ct,
        string? page = null,
        string? pageSize = null,
        string? search = null)
    {
        var (paging, errors) = ListQuery.Parse(page, pageSize, search);
        if (paging is null)
        {
            return TypedResults.ValidationProblem(errors!);
        }

        var query = db.Contacts.AsNoTracking();
        if (paging.Search is { } term)
        {
            var pattern = LikePattern.Contains(term);
            query = query.Where(c =>
                EF.Functions.Like(c.FirstName, pattern, LikePattern.EscapeChar)
                || EF.Functions.Like(c.LastName!, pattern, LikePattern.EscapeChar)
                || EF.Functions.Like(c.Email!, pattern, LikePattern.EscapeChar));
        }

        var totalCount = await query.CountAsync(ct);

        var items = new List<ContactResponse>();
        if (paging.TryGetSkip(out var skip))
        {
            var contacts = await query
                .OrderBy(c => c.LastName == null)
                .ThenBy(c => c.LastName)
                .ThenBy(c => c.FirstName)
                .ThenBy(c => c.Id)
                .Skip(skip)
                .Take(paging.PageSize)
                .ToListAsync(ct);
            items.AddRange(contacts.Select(ContactResponse.From));
        }

        return TypedResults.Ok(new PagedResponse<ContactResponse>(items, paging.Page, paging.PageSize, totalCount));
    }

    private static async Task<Results<Ok<ContactResponse>, NotFound>> GetContact(
        Guid id,
        AppDbContext db,
        CancellationToken ct)
    {
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

        return contact is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ContactResponse.From(contact));
    }

    private static async Task<Results<Created<ContactResponse>, ValidationProblem, ProblemHttpResult>> CreateContact(
        CreateContactRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var (input, errors) = ContactInput.Parse(request);
        if (input is null)
        {
            return TypedResults.ValidationProblem(errors!);
        }

        var now = time.GetUtcNow();
        var contact = new Contact
        {
            Id = Guid.CreateVersion7(now),
            FirstName = input.FirstName,
            LastName = input.LastName,
            Email = input.Email,
            Phone = input.Phone,
            Company = input.Company,
            Notes = input.Notes,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Contacts.Add(contact);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueConstraintViolation(ex))
        {
            return DuplicateEmailProblem();
        }

        return TypedResults.Created($"/api/contacts/{contact.Id}", ContactResponse.From(contact));
    }

    private static async Task<Results<Ok<ContactResponse>, ValidationProblem, ProblemHttpResult>> UpdateContact(
        Guid id,
        UpdateContactRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var (input, errors) = ContactInput.Parse(request);
        if (input is null)
        {
            return TypedResults.ValidationProblem(errors!);
        }

        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
        }

        contact.FirstName = input.FirstName;
        contact.LastName = input.LastName;
        contact.Email = input.Email;
        contact.Phone = input.Phone;
        contact.Company = input.Company;
        contact.Notes = input.Notes;
        contact.UpdatedAt = time.GetUtcNow();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The contact was deleted between the load and the save.
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueConstraintViolation(ex))
        {
            return DuplicateEmailProblem();
        }

        return TypedResults.Ok(ContactResponse.From(contact));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteContact(
        Guid id,
        AppDbContext db,
        CancellationToken ct)
    {
        var deleted = await db.Contacts.Where(c => c.Id == id).ExecuteDeleteAsync(ct);

        return deleted == 0
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound)
            : TypedResults.NoContent();
    }

    private static ProblemHttpResult DuplicateEmailProblem() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "A contact with this email already exists.");
}
