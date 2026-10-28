using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Zai.Api.Domain;

namespace Zai.Api.Contracts;

public sealed class SensorInput
{
    [Required(ErrorMessage = "Nazwa jest wymagana.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Nazwa musi mieć od 1 do 100 znaków.")]
    public string? Name { get; init; }

    [Required(ErrorMessage = "Seria jest wymagana.")]
    public int? SeriesId { get; init; }
}

public sealed record SensorDto(
    int Id,
    string Name,
    int SeriesId,
    string SeriesName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastMeasurementAt)
{
    public static readonly Expression<Func<Sensor, SensorDto>> Projection =
        s => new SensorDto(s.Id, s.Name, s.SeriesId, s.Series.Name, s.CreatedAt, s.LastMeasurementAt);
}

public sealed record SensorCreatedDto(
    int Id,
    string Name,
    int SeriesId,
    string SeriesName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastMeasurementAt,
    string ApiKey);
