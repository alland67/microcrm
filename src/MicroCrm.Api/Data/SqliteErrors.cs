using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MicroCrm.Api.Data;

public static class SqliteErrors
{
    private const int SqliteConstraintUnique = 2067;

    public static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique };
}
