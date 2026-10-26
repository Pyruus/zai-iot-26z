using Microsoft.AspNetCore.Mvc;

namespace Zai.Api.Infrastructure;

public sealed record FieldError(string Field, string Message);

public static class ApiProblems
{
    public static ObjectResult Create(HttpContext context, int status, string title, string? detail = null,
        IEnumerable<FieldError>? errors = null, IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
        };
        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        foreach (var (key, value) in extensions ?? new Dictionary<string, object?>())
        {
            problem.Extensions[key] = value;
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    public static ObjectResult NotFound(HttpContext context, string detail) =>
        Create(context, StatusCodes.Status404NotFound, "Nie znaleziono", detail);

    public static ObjectResult Unprocessable(HttpContext context, IReadOnlyCollection<FieldError> errors) =>
        Create(context, StatusCodes.Status422UnprocessableEntity, "Niepoprawne dane",
            string.Join(" ", errors.Select(e => e.Message)), errors);

    public static IActionResult InvalidModelState(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error => new FieldError(
                FieldName(entry.Key),
                string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Niepoprawna wartość." : error.ErrorMessage)))
            .ToList();

        return Create(context.HttpContext, StatusCodes.Status400BadRequest, "Niepoprawne żądanie",
            "Treść żądania zawiera błędy.", errors);
    }

    private static string FieldName(string key)
    {
        var name = key.StartsWith("$.") ? key[2..] : key.TrimStart('$');
        if (name.Length == 0)
        {
            return "body";
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
