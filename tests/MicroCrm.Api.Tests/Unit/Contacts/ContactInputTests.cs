using MicroCrm.Api.Features.Contacts;

namespace MicroCrm.Api.Tests.Unit.Contacts;

public sealed class ContactInputTests
{
    private static readonly CreateContactRequest Valid = new("Ada", null, null, null, null, null);

    [Fact]
    public void Parse_TrimsAllFields_AC005()
    {
        var request = new CreateContactRequest(
            FirstName: "  Ada\t",
            LastName: "\n Lovelace  ",
            Email: "  ada@example.com ",
            Phone: " +44 20 7946 0000  ",
            Company: "   Analytical Engines Ltd ",
            Notes: "  First programmer.\n");

        var (input, errors) = ContactInput.Parse(request);

        Assert.Null(errors);
        Assert.NotNull(input);
        Assert.Equal("Ada", input.FirstName);
        Assert.Equal("Lovelace", input.LastName);
        Assert.Equal("ada@example.com", input.Email);
        Assert.Equal("+44 20 7946 0000", input.Phone);
        Assert.Equal("Analytical Engines Ltd", input.Company);
        Assert.Equal("First programmer.", input.Notes);
    }

    [Theory]
    [InlineData("lastName", "")]
    [InlineData("lastName", "   ")]
    [InlineData("email", "")]
    [InlineData("email", "   ")]
    [InlineData("phone", "")]
    [InlineData("phone", "   ")]
    [InlineData("company", "")]
    [InlineData("company", "   ")]
    [InlineData("notes", "")]
    [InlineData("notes", "   ")]
    public void Parse_EmptyOrWhitespaceOptional_BecomesNull_AC006(string field, string value)
    {
        var request = field switch
        {
            "lastName" => Valid with { LastName = value },
            "email" => Valid with { Email = value },
            "phone" => Valid with { Phone = value },
            "company" => Valid with { Company = value },
            "notes" => Valid with { Notes = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

        var (input, errors) = ContactInput.Parse(request);

        Assert.Null(errors);
        Assert.NotNull(input);
        Assert.Equal("Ada", input.FirstName);
        var actual = field switch
        {
            "lastName" => input.LastName,
            "email" => input.Email,
            "phone" => input.Phone,
            "company" => input.Company,
            "notes" => input.Notes,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };
        Assert.Null(actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_MissingOrBlankFirstName_ReturnsFirstNameError_AC007(string? firstName)
    {
        var request = Valid with { FirstName = firstName };

        var (input, errors) = ContactInput.Parse(request);

        Assert.Null(input);
        Assert.NotNull(errors);
        Assert.True(errors.ContainsKey("firstName"), "errors is missing the 'firstName' key");
        Assert.NotEmpty(errors["firstName"]);
    }

    // Builds a request whose named field holds the given value; every other field is valid.
    private static CreateContactRequest WithField(string field, string value) => field switch
    {
        "firstName" => Valid with { FirstName = value },
        "lastName" => Valid with { LastName = value },
        "email" => Valid with { Email = value },
        "phone" => Valid with { Phone = value },
        "company" => Valid with { Company = value },
        "notes" => Valid with { Notes = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    // Exactly `length` chars, one '@', no whitespace, so it stays well-formed when the email format rule lands.
    private static string EmailOfLength(int length)
        => new string('a', length - "@example.com".Length) + "@example.com";

    private static string ValueOfLength(string field, int length)
        => field == "email" ? EmailOfLength(length) : new string('x', length);

    private static string? Read(ContactInput input, string field) => field switch
    {
        "firstName" => input.FirstName,
        "lastName" => input.LastName,
        "email" => input.Email,
        "phone" => input.Phone,
        "company" => input.Company,
        "notes" => input.Notes,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    [Theory]
    [InlineData("firstName", 100)]
    [InlineData("lastName", 100)]
    [InlineData("email", 254)]
    [InlineData("phone", 50)]
    [InlineData("company", 200)]
    [InlineData("notes", 4000)]
    public void Parse_FieldOverMax_ReturnsFieldError_AC008(string field, int max)
    {
        var value = ValueOfLength(field, max + 1);
        Assert.Equal(max + 1, value.Length);

        var (input, errors) = ContactInput.Parse(WithField(field, value));

        Assert.Null(input);
        Assert.NotNull(errors);
        Assert.True(errors.ContainsKey(field), $"errors is missing the '{field}' key");
        Assert.NotEmpty(errors[field]);
    }

    [Fact]
    public void Parse_SeveralFieldsOverMax_ReturnsErrorForEach_AC008()
    {
        var request = Valid with
        {
            LastName = new string('x', 101),
            Phone = new string('x', 51),
            Notes = new string('x', 4001),
        };

        var (input, errors) = ContactInput.Parse(request);

        Assert.Null(input);
        Assert.NotNull(errors);
        Assert.Equal(["lastName", "notes", "phone"], errors.Keys.Order().ToArray());
        Assert.All(errors.Values, messages => Assert.NotEmpty(messages));
    }

    // Guard: no limits exist yet, so this passes at RED. It catches an off-by-one (>= max) or measuring
    // before trimming (max wrapped in whitespace would be rejected) once the limits are added.
    [Theory]
    [InlineData("firstName", 100, false)]
    [InlineData("firstName", 100, true)]
    [InlineData("lastName", 100, false)]
    [InlineData("lastName", 100, true)]
    [InlineData("email", 254, false)]
    [InlineData("email", 254, true)]
    [InlineData("phone", 50, false)]
    [InlineData("phone", 50, true)]
    [InlineData("company", 200, false)]
    [InlineData("company", 200, true)]
    [InlineData("notes", 4000, false)]
    [InlineData("notes", 4000, true)]
    public void Parse_FieldAtMax_IsValid_AC009(string field, int max, bool wrapInWhitespace)
    {
        var value = ValueOfLength(field, max);
        Assert.Equal(max, value.Length);
        var raw = wrapInWhitespace ? "  " + value + "\t\n" : value;

        var (input, errors) = ContactInput.Parse(WithField(field, raw));

        Assert.Null(errors);
        Assert.NotNull(input);
        Assert.Equal(value, Read(input, field));
    }

    [Theory]
    [InlineData("a", false)]
    [InlineData("a@", false)]
    [InlineData("@b", false)]
    [InlineData("a@@b", false)]
    [InlineData("a@b@c", false)]
    [InlineData("a b@c", false)]
    [InlineData("a\u00A0b@c", false)] // interior non-breaking space: char.IsWhiteSpace is true
    [InlineData("a@b", true)]
    [InlineData("Ada.Lovelace@Example.com", true)]
    public void IsValidEmail_Rules_AC010(string email, bool expected)
    {
        Assert.Equal(expected, ContactInput.IsValidEmail(email));
    }

    [Fact]
    public void Parse_MultipleInvalidFields_ReturnsAllErrors_AC014()
    {
        var request = Valid with
        {
            FirstName = null,
            Email = "not-an-email",
            Phone = new string('1', 51),
        };

        var (input, errors) = ContactInput.Parse(request);

        Assert.Null(input);
        Assert.NotNull(errors);
        Assert.Equal(["email", "firstName", "phone"], errors.Keys.Order().ToArray());
        Assert.All(errors.Values, messages => Assert.NotEmpty(messages));
    }
}
