using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zai.Api.Data;
using Zai.Api.Infrastructure;

namespace Zai.Api.Auth;

public sealed class SensorApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db,
    EventLogger events)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string HeaderName = "X-API-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? key = Request.Headers[HeaderName];
        if (string.IsNullOrEmpty(key))
        {
            return AuthenticateResult.NoResult();
        }

        var hash = Secrets.Hash(key);
        var sensor = await db.Sensors
            .Where(s => s.ApiKeyHash == hash && s.RevokedAt == null)
            .Select(s => new { s.Id, s.SeriesId, s.Name })
            .FirstOrDefaultAsync();

        if (sensor is null)
        {
            await events.WarnAsync(EventKinds.InvalidApiKey,
                $"Odrzucono wynik przesłany nieprawidłowym lub unieważnionym kluczem API (adres {Context.Connection.RemoteIpAddress}).");
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, sensor.Name),
            new Claim(ClaimsExtensions.SensorIdClaim, sensor.Id.ToString()),
            new Claim(ClaimsExtensions.SeriesIdClaim, sensor.SeriesId.ToString()),
        ], Scheme.Name);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"ApiKey header=\"{HeaderName}\"";
        return Task.CompletedTask;
    }
}
