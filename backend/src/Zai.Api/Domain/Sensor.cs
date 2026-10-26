namespace Zai.Api.Domain;

public class Sensor
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    public required string ApiKeyHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastMeasurementAt { get; set; }
}
