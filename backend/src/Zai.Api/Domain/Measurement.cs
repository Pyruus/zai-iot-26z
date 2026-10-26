namespace Zai.Api.Domain;

public class Measurement
{
    public long Id { get; set; }
    public int SeriesId { get; set; }
    public int? SensorId { get; set; }
    public double Value { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
