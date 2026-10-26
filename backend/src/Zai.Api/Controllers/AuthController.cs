using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zai.Api.Auth;
using Zai.Api.Contracts;
using Zai.Api.Data;
using Zai.Api.Domain;
using Zai.Api.Infrastructure;

namespace Zai.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    IPasswordHasher hasher,
    IOptions<AuthOptions> options,
    TimeProvider clock,
    EventLogger events) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var admin = await db.Admins.FirstOrDefaultAsync(a => a.Username == request.Username);
        if (!hasher.Verify(admin?.PasswordHash, request.Password!))
        {
            await events.WarnAsync(EventKinds.LoginFailed, $"Nieudane logowanie użytkownika „{request.Username}”.");
            return ApiProblems.Create(HttpContext, StatusCodes.Status401Unauthorized, "Brak autoryzacji",
                "Niepoprawna nazwa użytkownika lub hasło.");
        }

        var token = Secrets.GenerateToken();
        var now = clock.GetUtcNow();
        var ttl = options.Value.SessionTtlSeconds;
        db.Sessions.Add(new Session
        {
            AdminId = admin!.Id,
            TokenHash = Secrets.Hash(token),
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(ttl),
        });
        await db.SaveChangesAsync();

        Response.Cookies.Append(options.Value.CookieName, token, SessionCookie(TimeSpan.FromSeconds(ttl)));
        return Ok(new LoginResponse(token, "Bearer", ttl));
    }

    [AdminOnly]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var sessionId = User.SessionId();
        var now = clock.GetUtcNow();
        await db.Sessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));

        Response.Cookies.Delete(options.Value.CookieName, SessionCookie(null));
        return NoContent();
    }

    [AdminOnly]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(PasswordChangeRequest request)
    {
        var admin = await db.Admins.SingleAsync(a => a.Id == User.AdminId());
        if (!hasher.Verify(admin.PasswordHash, request.CurrentPassword!))
        {
            return ApiProblems.Create(HttpContext, StatusCodes.Status403Forbidden, "Odmowa dostępu",
                "Podane bieżące hasło jest niepoprawne.");
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return ApiProblems.Create(HttpContext, StatusCodes.Status422UnprocessableEntity, "Niepoprawne dane",
                "Nowe hasło musi różnić się od bieżącego.",
                [new FieldError("newPassword", "Nowe hasło musi różnić się od bieżącego.")]);
        }

        var now = clock.GetUtcNow();
        admin.PasswordHash = hasher.Hash(request.NewPassword!);
        admin.PasswordChangedAt = now;
        await db.SaveChangesAsync();

        var sessionId = User.SessionId();
        await db.Sessions
            .Where(s => s.AdminId == admin.Id && s.Id != sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));

        return NoContent();
    }

    [AdminOnly]
    [HttpGet("me")]
    public CurrentAdminResponse Me() => new(User.Identity!.Name!);

    private static CookieOptions SessionCookie(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api",
        MaxAge = maxAge,
        IsEssential = true,
    };
}
