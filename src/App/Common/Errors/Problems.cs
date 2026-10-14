using Npgsql;

namespace App.Common.Errors;

/// <summary>Problem details with the stable types listed under Status codes in the technical spec.</summary>
public static class Problems
{
    public static IResult NotFound() =>
        Problem(StatusCodes.Status404NotFound, "not-found", "Not found");

    public static IResult Forbidden() =>
        Problem(StatusCodes.Status403Forbidden, "forbidden", "Forbidden");

    /// <summary>A valid request in the wrong state, such as engagement-closed.</summary>
    public static IResult Conflict(string type, string? detail = null, IDictionary<string, object?>? extensions = null) =>
        Problem(StatusCodes.Status409Conflict, type, "Conflict", detail, extensions);

    /// <summary>A rule the request breaks, such as invalid-lead or invalid-reference.</summary>
    public static IResult BadRequest(string type, string? detail = null) =>
        Problem(StatusCodes.Status400BadRequest, type, "Bad request", detail);

    /// <summary>Field rules: one or more messages per field.</summary>
    public static IResult Validation(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, type: "validation", title: "Invalid request");

    public static IResult Problem(
        int status, string type, string title, string? detail = null, IDictionary<string, object?>? extensions = null) =>
        Results.Problem(statusCode: status, type: type, title: title, detail: detail, extensions: extensions);

    /// <summary>True when saving broke the named unique index or constraint.</summary>
    public static bool IsUniqueViolation(Exception exception, string constraint) =>
        exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == constraint;
}
