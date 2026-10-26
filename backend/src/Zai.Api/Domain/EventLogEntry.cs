namespace Zai.Api.Domain;

public class EventLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string Level { get; set; }
    public required string Kind { get; set; }
    public int? SeriesId { get; set; }
    public int? SensorId { get; set; }
    public required string Message { get; set; }
    public string? Payload { get; set; }
}
