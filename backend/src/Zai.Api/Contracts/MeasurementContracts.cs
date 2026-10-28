using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Zai.Api.Domain;

namespace Zai.Api.Contracts;

public sealed class MeasurementInput
{
    [Required(ErrorMessage = "Wartość jest wymagana.")]
    public double? Value { get; init; }

    public DateTimeOffset? Timestamp { get; init; }
}

public sealed record MeasurementDto(long Id, int SeriesId, int? SensorId, double Value, DateTimeOffset Timestamp)
{
    public static readonly Expression<Func<Measurement, MeasurementDto>> Projection =
        m => new MeasurementDto(m.Id, m.SeriesId, m.SensorId, m.Value, m.Timestamp);

    public static MeasurementDto From(Measurement m) => new(m.Id, m.SeriesId, m.SensorId, m.Value, m.Timestamp);
}

public sealed record EventLogDto(
    long Id,
    DateTimeOffset OccurredAt,
    string Level,
    string Kind,
    int? SeriesId,
    int? SensorId,
    string Message);
