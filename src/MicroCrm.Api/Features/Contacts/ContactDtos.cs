namespace MicroCrm.Api.Features.Contacts;

public sealed record CreateContactRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Company,
    string? Notes);

public sealed record ContactResponse(
    Guid Id,
    string FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Company,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ContactResponse From(Contact contact) => new(
        contact.Id,
        contact.FirstName,
        contact.LastName,
        contact.Email,
        contact.Phone,
        contact.Company,
        contact.Notes,
        contact.CreatedAt,
        contact.UpdatedAt);
}
