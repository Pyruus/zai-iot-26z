using Microsoft.AspNetCore.Mvc;

namespace Zai.Api.Infrastructure;

public sealed record FieldError(string Field, string Message);

public static class ApiProblems
{
    private static readonly Dictionary<int, (string Title, string Detail)> DefaultTexts = new()
    {
        [400] = ("Niepoprawne żądanie", "Żądanie jest niepoprawne."),
        [401] = ("Brak autoryzacji", "Wymagane jest uwierzytelnienie: token administratora lub klucz API czujnika."),
        [403] = ("Odmowa dostępu", "Brak uprawnień do wykonania tej operacji."),
        [404] = ("Nie znaleziono", "Zasób nie istnieje."),
        [405] = ("Niedozwolona metoda", "Ta metoda HTTP nie jest obsługiwana dla tego zasobu."),
        [415] = ("Nieobsługiwany format treści", "Treść żądania musi mieć format application/json."),
        [429] = ("Zbyt wiele żądań", "Przekroczono limit żądań. Spróbuj ponownie za chwilę."),
        [500] = ("Błąd serwera", "Wystąpił nieoczekiwany błąd serwera."),
    };

    public static void Localize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Instance ??= context.HttpContext.Request.Path;
        if (problem.Status is { } status && DefaultTexts.TryGetValue(status, out var text) && problem.Detail is null)
        {
            problem.Title = text.Title;
            problem.Detail = text.Detail;
        }
    }

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
