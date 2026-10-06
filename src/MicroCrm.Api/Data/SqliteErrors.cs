using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MicroCrm.Api.Data;

public static class SqliteErrors
{
    private const int SqliteConstraintUnique = 2067;
    private const int SqliteConstraintForeignKey = 787;

    public static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique };

    public static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintForeignKey };
}
