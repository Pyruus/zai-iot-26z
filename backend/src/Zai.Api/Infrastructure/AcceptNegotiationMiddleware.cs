using Microsoft.Net.Http.Headers;

namespace Zai.Api.Infrastructure;

public sealed class AcceptNegotiationMiddleware(RequestDelegate next)
{
    private static readonly MediaTypeHeaderValue[] Supported =
    [
        new("application/json"),
        new("application/problem+json"),
        new("text/event-stream"),
    ];

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api") && !IsAcceptable(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
            return Task.CompletedTask;
        }

        return next(context);
    }

    private static bool IsAcceptable(HttpRequest request)
    {
        var accept = request.GetTypedHeaders().Accept;
        if (accept.Count == 0)
        {
            return true;
        }

        return accept.Any(range =>
            range.Quality is not 0 && Supported.Any(type => type.IsSubsetOf(range)));
    }
}
