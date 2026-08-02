using System.Security.Cryptography;
using System.Text;

namespace EdgeCompanion.Host;

public static class ProcessInfo
{
    public static DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
}

public sealed record ApiError(string Module, string Code, string Message);

public sealed record ApiResponse<T>(T? Data, DateTimeOffset ObservedAt, IEnumerable<ApiError>? Errors = null);

public static class ApiEnvelope
{
    public static ApiResponse<T> From<T>(T data, IEnumerable<ApiError?>? errors = null) =>
        new(data, DateTimeOffset.UtcNow, errors?.Where(error => error is not null).Cast<ApiError>().ToArray());

    public static ApiResponse<object> Error(string code, string message) =>
        new(null, DateTimeOffset.UtcNow, [new ApiError("host", code, message)]);
}

public sealed record SafeResult<T>(T? Value, ApiError? Error)
{
    public static async Task<SafeResult<T>> Capture(string module, Func<Task<T>> operation)
    {
        try
        {
            return new(await operation(), null);
        }
        catch (Exception exception)
        {
            return new(default, new ApiError(module, "unavailable", exception.Message));
        }
    }
}

public static class OriginPolicy
{
    public static bool IsAllowed(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        if (origin is "null" or "file://") return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host == "127.0.0.1";
    }
}

public static class ActionAuthorization
{
    public static bool IsAllowed(HttpRequest request, string? expectedToken) =>
        !string.IsNullOrEmpty(expectedToken)
        && FixedTimeEquals(request.Headers["X-Edge-Token"].ToString(), expectedToken);

    private static bool FixedTimeEquals(string suppliedToken, string expectedToken)
    {
        var supplied = Encoding.UTF8.GetBytes(suppliedToken);
        var expected = Encoding.UTF8.GetBytes(expectedToken);
        return supplied.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }
}

public sealed class ModuleException(string code, string message, int statusCode = 503) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record PauseRequest(int Minutes);

public sealed class ProxyResponseResult(HttpResponseMessage response, string fallbackContentType) : IResult
{
    public async Task ExecuteAsync(HttpContext context)
    {
        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? fallbackContentType;
            context.Response.ContentLength = response.Content.Headers.ContentLength;
            if (response.Content.Headers.ContentRange is not null) context.Response.Headers.ContentRange = response.Content.Headers.ContentRange.ToString();
            if (response.Headers.AcceptRanges.Contains("bytes")) context.Response.Headers.AcceptRanges = "bytes";
            await using var stream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
    }
}
