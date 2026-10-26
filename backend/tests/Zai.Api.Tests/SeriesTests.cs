using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zai.Api.Domain;

namespace Zai.Api.Tests;

[Collection(ApiCollection.Name)]
public class SeriesTests(ApiFactory factory)
{
    private static object Input(string? name = null, double min = 0, double max = 100, string color = "#1f77b4",
        string? icon = null, string? unit = null) =>
        new { name = name ?? $"Seria-{Guid.NewGuid():N}", minValue = min, maxValue = max, color, icon, unit };

    private async Task<(HttpClient Client, JsonElement Series, string Location)> CreateSeriesAsync(object? input = null)
    {
        var client = await factory.CreateAdminClientAsync();
        var response = await client.PostAsJsonAsync("/api/series", input ?? Input());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var series = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (client, series, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task List_IsPublic()
    {
        await CreateSeriesAsync();

        var response = await factory.CreateApiClient().GetAsync("/api/series");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var list = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(list.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Create_Returns201WithLocationAndBody()
    {
        var (_, series, location) = await CreateSeriesAsync(Input(name: "Warszawa", min: -40, max: 45, unit: "°C", icon: "circle"));

        Assert.Equal($"/api/series/{series.GetProperty("id").GetInt32()}", location);
        Assert.Equal("Warszawa", series.GetProperty("name").GetString());
        Assert.Equal(-40, series.GetProperty("minValue").GetDouble());
        Assert.Equal(JsonValueKind.Number, series.GetProperty("maxValue").ValueKind);
        Assert.Equal("°C", series.GetProperty("unit").GetString());

        var fetched = await factory.CreateApiClient().GetFromJsonAsync<JsonElement>(location);
        Assert.Equal("Warszawa", fetched.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/series", Input());

        await ProblemAssert.IsProblem(response, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    public async Task Create_WithMinNotBelowMax_Returns422(double min, double max)
    {
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/series", Input(min: min, max: max));

        await ProblemAssert.IsProblem(response, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_WithUnknownIcon_Returns422()
    {
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/series", Input(icon: "unicorn"));

        await ProblemAssert.IsProblem(response, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_WithInvalidFields_Returns400WithFieldErrors()
    {
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/series", new { minValue = 0, maxValue = 1, color = "blue" });

        await ProblemAssert.IsProblem(response, HttpStatusCode.BadRequest);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var fields = problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("name", fields);
        Assert.Contains("color", fields);
    }

    [Fact]
    public async Task Create_WithWrongValueType_Returns400()
    {
        var client = await factory.CreateAdminClientAsync();
        var body = new StringContent("""{"name":"x","minValue":"abc","maxValue":1,"color":"#000000"}""", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/series", body);

        await ProblemAssert.IsProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithMalformedJson_Returns400WithoutStackTrace()
    {
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostAsync("/api/series", new StringContent("""{"name":""", Encoding.UTF8, "application/json"));

        await ProblemAssert.IsProblem(response, HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("   at ", text);
        Assert.DoesNotContain("\"stack\"", text);
    }

    [Fact]
    public async Task Create_WithTextPlain_Returns415()
    {
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PostAsync("/api/series", new StringContent("name=x", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReplacesAllFields()
    {
        var (client, series, location) = await CreateSeriesAsync(Input(icon: "star", unit: "kWh"));

        var response = await client.PutAsJsonAsync(location, Input(name: "Nowa nazwa", color: "#ff7f0e"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(series.GetProperty("id").GetInt32(), updated.GetProperty("id").GetInt32());
        Assert.Equal("Nowa nazwa", updated.GetProperty("name").GetString());
        Assert.Equal("#ff7f0e", updated.GetProperty("color").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("icon").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("unit").ValueKind);
    }

    [Fact]
    public async Task Update_ExcludingStoredMeasurements_Returns409()
    {
        var (client, series, location) = await CreateSeriesAsync();
        var id = series.GetProperty("id").GetInt32();
        await factory.WithDbAsync(async db =>
        {
            db.Measurements.Add(new Measurement { SeriesId = id, Value = 21.5, Timestamp = DateTimeOffset.UtcNow.AddHours(-1), ReceivedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

        var conflict = await client.PutAsJsonAsync(location, Input(min: 50, max: 60));
        var allowed = await client.PutAsJsonAsync(location, Input(min: 21.5, max: 22));

        await ProblemAssert.IsProblem(conflict, HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var client = await factory.CreateAdminClientAsync();

        var response = await client.PutAsJsonAsync("/api/series/999999999", Input());

        await ProblemAssert.IsProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RemovesSeriesWithMeasurements()
    {
        var (client, series, location) = await CreateSeriesAsync();
        var id = series.GetProperty("id").GetInt32();
        await factory.WithDbAsync(async db =>
        {
            db.Measurements.Add(new Measurement { SeriesId = id, Value = 1, Timestamp = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

        var unauthorized = await factory.CreateApiClient().DeleteAsync(location);
        var deleted = await client.DeleteAsync(location);
        var again = await client.DeleteAsync(location);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await ProblemAssert.IsProblem(again, HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(location)).StatusCode);
        await factory.WithDbAsync(async db => Assert.False(await db.Measurements.AnyAsync(m => m.SeriesId == id)));
    }

    [Theory]
    [InlineData("/api/series/999999999")]
    [InlineData("/api/series/abc")]
    [InlineData("/api/series/99999999999")]
    public async Task Get_Missing_Returns404Problem(string path)
    {
        var response = await factory.CreateApiClient().GetAsync(path);

        await ProblemAssert.IsProblem(response, HttpStatusCode.NotFound);
    }
}
