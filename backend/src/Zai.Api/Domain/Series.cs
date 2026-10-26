namespace Zai.Api.Domain;

public class Series
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public required string Color { get; set; }
    public string? Icon { get; set; }
    public string? Unit { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
