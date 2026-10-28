using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Zai.Api.Tests;

[Collection(ApiCollection.Name)]
public class SensorsTests(ApiFactory factory)
{
    [Fact]
    public async Task Register_ReturnsKeyOnlyOnce()
    {
        var fixture = await factory.SeriesWithSensorAsync();

        Assert.True(fixture.ApiKey.Length >= 32);
        Assert.Equal($"/api/sensors/{fixture.SensorId}", fixture.SensorLocation);

        var list = await fixture.Admin.GetAsync("/api/sensors");
        var one = await fixture.Admin.GetAsync(fixture.SensorLocation);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        var listText = await list.Content.ReadAsStringAsync();
        var oneText = await one.Content.ReadAsStringAsync();
        Assert.Contains($"\"id\":{fixture.SensorId}", listText);
        Assert.DoesNotContain(fixture.ApiKey, listText);
        Assert.DoesNotContain(fixture.ApiKey, oneText);
        Assert.DoesNotContain("hash", listText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fixture.SeriesId, JsonDocument.Parse(oneText).RootElement.GetProperty("seriesId").GetInt32());
    }

    [Fact]
    public async Task Register_ForMissingSeries_Returns422()
    {
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/sensors", new { name = "x", seriesId = 999999999 });

        await ProblemAssert.IsProblem(response, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Unregister_DisablesKeyButKeepsMeasurements()
    {
        var fixture = await factory.SeriesWithSensorAsync();
        var measurement = await factory.SendOkAsync(fixture.ApiKey, new { value = 21.5, timestamp = "2026-01-10T11:00:00Z" });

        var deleted = await fixture.Admin.DeleteAsync(fixture.SensorLocation);
        var rejected = await factory.SendAsync(fixture.ApiKey, new { value = 1 });
        var stillThere = await factory.CreateApiClient().GetAsync($"/api/measurements/{measurement.GetProperty("id").GetInt64()}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await ProblemAssert.IsProblem(rejected, HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Admin.GetAsync(fixture.SensorLocation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Admin.DeleteAsync(fixture.SensorLocation)).StatusCode);
    }

    [Fact]
    public async Task DeletingSeries_RemovesItsSensors()
    {
        var fixture = await factory.SeriesWithSensorAsync();

        await fixture.Admin.DeleteAsync($"/api/series/{fixture.SeriesId}");

        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Admin.GetAsync(fixture.SensorLocation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.SendAsync(fixture.ApiKey, new { value = 1 })).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/sensors")]
    [InlineData("GET", "/api/sensors/1")]
    [InlineData("POST", "/api/sensors")]
    [InlineData("DELETE", "/api/sensors/1")]
    public async Task Endpoints_RequireAdmin(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { name = "x", seriesId = 1 });
        }

        var response = await factory.CreateApiClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
