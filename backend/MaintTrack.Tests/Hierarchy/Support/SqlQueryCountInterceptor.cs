using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MaintTrack.Tests.Hierarchy.Support;

public sealed class SqlQueryCountInterceptor : DbCommandInterceptor
{
    private int _selectCount;

    public int SelectCount => _selectCount;

    public void Reset() => _selectCount = 0;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        if (IsSelect(command))
        {
            _selectCount++;
        }

        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (IsSelect(command))
        {
            _selectCount++;
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private static bool IsSelect(DbCommand command) =>
        command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
}
