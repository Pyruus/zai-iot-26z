using Microsoft.AspNetCore.Authorization;

namespace Zai.Api.Auth;

public sealed class AdminOnlyAttribute : AuthorizeAttribute
{
    public AdminOnlyAttribute() => Policy = AuthPolicies.Admin;
}
