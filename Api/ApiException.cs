namespace AMS.Api;

/** Business-rule exception thrown by the service layer; the exception
    handler maps it to an HTTP status with a `{ message }` body — the exact
    shape the frontend surfaces to the counter. */
public sealed class ApiException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;

    public static ApiException BadRequest(string message) => new(message, 400);
    public static ApiException NotFound(string message) => new(message, 404);
    public static ApiException Conflict(string message) => new(message, 409);
    public static ApiException Unauthorized(string message) => new(message, 401);
}