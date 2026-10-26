using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Zai.Api.Auth;
using Zai.Api.Data;
using Zai.Api.Domain;

namespace Zai.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "Test-Password-123!";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("timescale/timescaledb:latest-pg17").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("Auth:LoginPermitPerMinute", "10000");
    }

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        await EnsureAdminAsync(AdminUser, AdminPassword);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    public async Task<HttpClient> CreateAdminClientAsync(string username = AdminUser, string password = AdminPassword)
    {
        var client = CreateApiClient();
        var token = await LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<string> LoginAsync(HttpClient client, string username = AdminUser,
        string password = AdminPassword)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return body!["accessToken"].ToString()!;
    }

    public async Task EnsureAdminAsync(string username, string password)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Admins.AnyAsync(a => a.Username == username))
        {
            return;
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Admins.Add(new Admin { Username = username, PasswordHash = hasher.Hash(password), CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
