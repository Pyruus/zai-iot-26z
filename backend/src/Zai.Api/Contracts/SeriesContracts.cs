using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Zai.Api.Domain;
using Zai.Api.Infrastructure;

namespace Zai.Api.Contracts;

public sealed class SeriesInput
{
    [Required(ErrorMessage = "Nazwa jest wymagana.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Nazwa musi mieć od 1 do 100 znaków.")]
    public string? Name { get; init; }

    [Required(ErrorMessage = "Wartość minimalna jest wymagana.")]
    public double? MinValue { get; init; }

    [Required(ErrorMessage = "Wartość maksymalna jest wymagana.")]
    public double? MaxValue { get; init; }

    [Required(ErrorMessage = "Kolor jest wymagany.")]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Kolor musi mieć format #RRGGBB.")]
    public string? Color { get; init; }

    public string? Icon { get; init; }

    [StringLength(20, ErrorMessage = "Jednostka może mieć najwyżej 20 znaków.")]
    public string? Unit { get; init; }

    public List<FieldError> Validate()
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add(new FieldError("name", "Nazwa nie może składać się z samych spacji."));
        }

        if (MinValue >= MaxValue)
        {
            errors.Add(new FieldError("maxValue", "Wartość maksymalna musi być większa od minimalnej."));
        }

        if (Icon is not null && !SeriesIcons.Allowed.Contains(Icon))
        {
            errors.Add(new FieldError("icon", $"Dozwolone ikony: {string.Join(", ", SeriesIcons.Allowed)}."));
        }

        return errors;
    }

    public void ApplyTo(Series series)
    {
        series.Name = Name!.Trim();
        series.MinValue = MinValue!.Value;
        series.MaxValue = MaxValue!.Value;
        series.Color = Color!;
        series.Icon = Icon;
        series.Unit = string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim();
    }
}

public sealed record SeriesDto(int Id, string Name, double MinValue, double MaxValue, string Color, string? Icon, string? Unit)
{
    public static readonly Expression<Func<Series, SeriesDto>> Projection =
        s => new SeriesDto(s.Id, s.Name, s.MinValue, s.MaxValue, s.Color, s.Icon, s.Unit);

    public static SeriesDto From(Series s) => new(s.Id, s.Name, s.MinValue, s.MaxValue, s.Color, s.Icon, s.Unit);
}

public static class SeriesIcons
{
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>
    {
        "circle", "triangle", "rect", "rectRot", "rectRounded", "star", "cross", "crossRot",
    };
}
