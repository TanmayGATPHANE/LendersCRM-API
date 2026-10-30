using Microsoft.AspNetCore.Diagnostics;

namespace AMS.Api;

/** Renders any ApiException as `{ message }` with its status code, so the
    controllers never need try/catch for validation and business rules. */
public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not ApiException api) return false;
        context.Response.StatusCode = api.Status;
        await context.Response.WriteAsJsonAsync(new { message = api.Message }, ct);
        return true;
    }
}