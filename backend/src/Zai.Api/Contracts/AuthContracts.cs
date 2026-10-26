using System.ComponentModel.DataAnnotations;

namespace Zai.Api.Contracts;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Nazwa użytkownika jest wymagana.")]
    public string? Username { get; init; }

    [Required(ErrorMessage = "Hasło jest wymagane.")]
    public string? Password { get; init; }
}

public sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn);

public sealed class PasswordChangeRequest
{
    [Required(ErrorMessage = "Bieżące hasło jest wymagane.")]
    public string? CurrentPassword { get; init; }

    [Required(ErrorMessage = "Nowe hasło jest wymagane.")]
    [MinLength(8, ErrorMessage = "Nowe hasło musi mieć co najmniej 8 znaków.")]
    public string? NewPassword { get; init; }
}

public sealed record CurrentAdminResponse(string Username);
