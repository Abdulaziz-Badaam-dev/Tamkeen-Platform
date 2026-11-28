using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Tamkeen.API.Data;

public sealed class WriteConflictHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception error, CancellationToken ct)
    {
        if (error is not DbUpdateConcurrencyException &&
            error is not DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } }) return false;
        await Results.Problem(statusCode: 409, detail: "A competing request saved this change. Refresh before retrying.")
            .ExecuteAsync(context);
        return true;
    }
}
