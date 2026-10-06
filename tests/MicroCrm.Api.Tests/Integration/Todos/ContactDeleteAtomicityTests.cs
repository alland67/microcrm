using System.Net;

using MicroCrm.Api.Tests.Integration.Infrastructure;

namespace MicroCrm.Api.Tests.Integration.Todos;

// Own fixture: each test installs a trigger on the shared in-memory database and drops it in finally,
// so no other test class can observe it (ADR-0005).
public sealed class ContactDeleteAtomicityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task AssertFailedDeleteChangedNothingAsync(string triggerSql, string triggerName, string email)
    {
        using var client = factory.CreateClient();
        var contactId = await TodoStoreTests.CreateContactAsync(client, "Atomic", email);
        var todoA = Guid.CreateVersion7();
        var todoB = Guid.CreateVersion7();
        await TodoStoreTests.InsertTodoAsync(factory.ConnectionString, todoA, TodoStoreTests.Upper(contactId));
        await TodoStoreTests.InsertTodoAsync(factory.ConnectionString, todoB, TodoStoreTests.Upper(contactId));

        await factory.ExecuteSqlAsync(triggerSql);
        try
        {
            var response = await client.DeleteAsync($"/api/contacts/{contactId}", Ct);

            using var problem = await ProblemAssert.IsProblemAsync(response, 500);
            var raw = problem.RootElement.GetRawText();
            Assert.DoesNotContain("Sqlite", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Todos", raw, StringComparison.Ordinal);
        }
        finally
        {
            await factory.ExecuteSqlAsync($"DROP TRIGGER IF EXISTS {triggerName}");
        }

        var contactRows = await TodoStoreTests.ScalarAsync(
            factory.ConnectionString,
            "SELECT COUNT(*) FROM Contacts WHERE lower(Id) = $id",
            ("$id", contactId.ToString().ToLowerInvariant()));
        Assert.Equal(1L, contactRows);
        foreach (var id in new[] { todoA, todoB })
        {
            var row = await TodoStoreTests.ReadTodoAsync(factory.ConnectionString, id);
            Assert.NotNull(row);
            Assert.Equal(TodoStoreTests.Upper(contactId), row["ContactId"]);
        }
    }

    [Fact]
    public Task DeleteContact_FailsAfterUnlink_RollsBackRemovalAndUnlink_AC065() =>
        AssertFailedDeleteChangedNothingAsync(
            "CREATE TRIGGER trg_fail_after_unlink AFTER DELETE ON Contacts " +
            "WHEN (SELECT COUNT(*) FROM Todos WHERE ContactId = OLD.Id) = 0 " +
            "BEGIN SELECT RAISE(ABORT, 'x'); END",
            "trg_fail_after_unlink",
            "atomic.after.ac065@example.com");

    [Fact]
    public Task DeleteContact_FailsDuringUnlink_RollsBackRemoval_AC065() =>
        AssertFailedDeleteChangedNothingAsync(
            "CREATE TRIGGER trg_fail_during_unlink BEFORE UPDATE ON Todos " +
            "BEGIN SELECT RAISE(ABORT, 'x'); END",
            "trg_fail_during_unlink",
            "atomic.during.ac065@example.com");
}
