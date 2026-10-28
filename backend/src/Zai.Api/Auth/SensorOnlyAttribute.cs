using Microsoft.AspNetCore.Authorization;

namespace Zai.Api.Auth;

public sealed class SensorOnlyAttribute : AuthorizeAttribute
{
    public SensorOnlyAttribute() => Policy = AuthPolicies.Sensor;
}
