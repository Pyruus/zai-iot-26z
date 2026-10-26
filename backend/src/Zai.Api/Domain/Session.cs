namespace Zai.Api.Domain;

public class Session
{
    public long Id { get; set; }
    public int AdminId { get; set; }
    public Admin Admin { get; set; } = null!;
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
