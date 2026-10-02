namespace MicroCrm.Api.Features.Contacts;

public sealed record ContactInput(
    string FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Company,
    string? Notes)
{
    public const int FirstNameMax = 100, LastNameMax = 100, EmailMax = 254,
                     PhoneMax = 50, CompanyMax = 200, NotesMax = 4000;

    public static (ContactInput? Input, Dictionary<string, string[]>? Errors) Parse(CreateContactRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        var firstName = request.FirstName?.Trim();
        if (string.IsNullOrEmpty(firstName))
        {
            errors["firstName"] = ["First name is required."];
        }

        var lastName = TrimToNull(request.LastName);
        var email = TrimToNull(request.Email);
        var phone = TrimToNull(request.Phone);
        var company = TrimToNull(request.Company);
        var notes = TrimToNull(request.Notes);

        CheckMax(errors, "firstName", firstName, FirstNameMax);
        CheckMax(errors, "lastName", lastName, LastNameMax);
        CheckMax(errors, "email", email, EmailMax);
        CheckMax(errors, "phone", phone, PhoneMax);
        CheckMax(errors, "company", company, CompanyMax);
        CheckMax(errors, "notes", notes, NotesMax);

        if (email is not null && !IsValidEmail(email))
        {
            errors["email"] = ["Must be a valid email address."];
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new ContactInput(firstName!, lastName, email, phone, company, notes), null);
    }

    public static bool IsValidEmail(string trimmedEmail)
    {
        var at = trimmedEmail.IndexOf('@');
        return at > 0
            && at < trimmedEmail.Length - 1
            && trimmedEmail.IndexOf('@', at + 1) < 0
            && !trimmedEmail.Any(char.IsWhiteSpace);
    }

    private static void CheckMax(Dictionary<string, string[]> errors, string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
        {
            errors[field] = [$"Must be {max} characters or fewer."];
        }
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
