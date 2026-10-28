using System.Security.Claims;

namespace Zai.Api.Auth;

public static class ClaimsExtensions
{
    public const string SessionIdClaim = "sid";
    public const string SensorIdClaim = "sensor_id";
    public const string SeriesIdClaim = "series_id";

    public static int AdminId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static long SessionId(this ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(SessionIdClaim)!);

    public static int SensorId(this ClaimsPrincipal user) => int.Parse(user.FindFirstValue(SensorIdClaim)!);

    public static int SeriesId(this ClaimsPrincipal user) => int.Parse(user.FindFirstValue(SeriesIdClaim)!);
}
