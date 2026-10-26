using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Zai.Api.Tests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_ReturnsBearerTokenAndSecureCookie()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = ApiFactory.AdminUser, password = ApiFactory.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("accessToken").GetString()!.Length >= 32);
        Assert.Equal("Bearer", json.RootElement.GetProperty("tokenType").GetString());
        Assert.True(json.RootElement.GetProperty("expiresIn").GetInt32() > 0);

        var cookie = response.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = ApiFactory.AdminUser, password = "wrong-password" });

        await ProblemAssert.IsProblem(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithMissingFields_Returns400()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin" });

        await ProblemAssert.IsProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/auth/me");

        await ProblemAssert.IsProblem(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task Me_WithForgedToken_Returns401()
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJub25lIn0.eyJzdWIiOiJhZG1pbiJ9.");

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithSessionCookie_ReturnsUsername()
    {
        var client = factory.CreateApiClient();
        var token = await ApiFactory.LoginAsync(client);
        client.DefaultRequestHeaders.Add("Cookie", $"zai_session={token}");

        var response = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/auth/me");

        Assert.Equal(ApiFactory.AdminUser, response!["username"]);
    }

    [Fact]
    public async Task Logout_InvalidatesToken()
    {
        var client = await factory.CreateAdminClientAsync();

        var logout = await client.PostAsync("/api/auth/logout", null);
        var after = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Returns403()
    {
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PutAsJsonAsync("/api/auth/password",
            new { currentPassword = "wrong-password", newPassword = "NoweHaslo-123456" });

        await ProblemAssert.IsProblem(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangePassword_WithoutToken_Returns401()
    {
        var client = factory.CreateApiClient();

        var response = await client.PutAsJsonAsync("/api/auth/password",
            new { currentPassword = ApiFactory.AdminPassword, newPassword = "NoweHaslo-123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_UpdatesCredentialsAndRevokesOtherSessions()
    {
        const string user = "password-change";
        const string oldPassword = "Old-Password-123";
        const string newPassword = "New-Password-456";
        await factory.EnsureAdminAsync(user, oldPassword);
        var other = await factory.CreateAdminClientAsync(user, oldPassword);
        var client = await factory.CreateAdminClientAsync(user, oldPassword);

        var response = await client.PutAsJsonAsync("/api/auth/password",
            new { currentPassword = oldPassword, newPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/auth/me")).StatusCode);
        var oldLogin = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/login",
            new { username = user, password = oldPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        await ApiFactory.LoginAsync(factory.CreateApiClient(), user, newPassword);
    }

    [Fact]
    public async Task ChangePassword_WithTooShortPassword_Returns400()
    {
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PutAsJsonAsync("/api/auth/password",
            new { currentPassword = ApiFactory.AdminPassword, newPassword = "short" });

        await ProblemAssert.IsProblem(response, HttpStatusCode.BadRequest);
    }
}
