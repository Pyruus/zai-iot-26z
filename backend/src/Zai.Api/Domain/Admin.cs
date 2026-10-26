namespace Zai.Api.Domain;

public class Admin
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
}
