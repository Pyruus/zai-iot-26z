namespace Zai.Api.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public int SessionTtlSeconds { get; set; } = 7200;
    public string CookieName { get; set; } = "zai_session";
    public int LoginPermitPerMinute { get; set; } = 20;
}

public static class AuthSchemes
{
    public const string Admin = "AdminSession";
    public const string Sensor = "SensorApiKey";
}

public static class AuthPolicies
{
    public const string Admin = "Admin";
    public const string Sensor = "Sensor";
}

public static class RateLimitPolicies
{
    public const string Login = "login";
}
