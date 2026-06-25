using Microsoft.AspNetCore.Http;

namespace BlazorDX.MockReportServer;

/// <summary>
/// SSRS-style error payloads. Real SSRS returns a SOAP/HTML error page carrying
/// an <c>rs*</c> error code (e.g. <c>rsItemNotFound</c>, <c>rsParameterError</c>).
/// We emit a compact, deterministic body that names the same codes so clients
/// and tests can branch on them, paired with a representative HTTP status.
/// </summary>
public static class SsrsErrors
{
    /// <summary>The report path did not resolve to a catalog item.</summary>
    public const string ItemNotFound = "rsItemNotFound";

    /// <summary>A required parameter was missing or a supplied value was invalid.</summary>
    public const string ParameterError = "rsParameterError";

    /// <summary>The requested <c>rs:Format</c> is not one this server renders.</summary>
    public const string UnknownFormat = "rsUnknownFormat";

    /// <summary>The <c>rs:Command</c> value is not supported.</summary>
    public const string UnknownCommand = "rsUnknownCommand";

    /// <summary>No report path was supplied on the request.</summary>
    public const string MissingPath = "rsItemNotFound";

    /// <summary>
    /// Writes an SSRS-style error result: an HTTP status plus a small text body
    /// whose first token is the SSRS error code, e.g.
    /// <c>rsItemNotFound: The item '/Nope' cannot be found.</c>
    /// </summary>
    public static IResult Write(int statusCode, string code, string message)
    {
        var body = $"{code}: {message}";
        return Results.Content(body, "text/plain; charset=utf-8", statusCode: statusCode);
    }

    public static IResult NotFound(string path) =>
        Write(StatusCodes.Status404NotFound, ItemNotFound,
            $"The item '{path}' cannot be found. (rsItemNotFound)");

    public static IResult MissingReportPath() =>
        Write(StatusCodes.Status400BadRequest, MissingPath,
            "No report path was supplied. The path is the first query token, e.g. ?/Sales/Monthly.");

    public static IResult Parameter(string message) =>
        Write(StatusCodes.Status400BadRequest, ParameterError, message);

    public static IResult Format(string format) =>
        Write(StatusCodes.Status400BadRequest, UnknownFormat,
            $"The format '{format}' is not supported. Supported: HTML5, PDF, CSV, IMAGE. (rsUnknownFormat)");

    public static IResult Command(string command) =>
        Write(StatusCodes.Status400BadRequest, UnknownCommand,
            $"The command '{command}' is not supported. Supported: Render, ListChildren. (rsUnknownCommand)");
}
