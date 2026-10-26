using System.Security.Claims;

namespace Zai.Api.Auth;

public static class ClaimsExtensions
{
    public const string SessionIdClaim = "sid";

    public static int AdminId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static long SessionId(this ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(SessionIdClaim)!);
}
