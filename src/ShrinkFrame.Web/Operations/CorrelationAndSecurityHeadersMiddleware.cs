namespace ShrinkFrame.Web.Operations;

public sealed class CorrelationAndSecurityHeadersMiddleware(RequestDelegate next, ILogger<CorrelationAndSecurityHeadersMiddleware> logger)
{
    private const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName];
        var correlationId = supplied.Count == 1 && IsSafe(supplied[0]) ? supplied[0]! : context.TraceIdentifier;
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; object-src 'none'; " +
            "img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; " +
            "connect-src 'self' ws: wss:";
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            await next(context);
    }

    private static bool IsSafe(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
