using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Zai.Api.Tests;

public sealed record SensorFixture(HttpClient Admin, int SeriesId, int SensorId, string ApiKey, string SensorLocation);

public static class TestData
{
    public static object SeriesInput(double min = 0, double max = 100) =>
        new { name = $"Seria-{Guid.NewGuid():N}", minValue = min, maxValue = max, color = "#1f77b4" };

    public static async Task<SensorFixture> SeriesWithSensorAsync(this ApiFactory factory, double min = 0, double max = 100)
    {
        var admin = await factory.CreateAdminClientAsync();
        var series = await admin.PostAsJsonAsync("/api/series", SeriesInput(min, max));
        Assert.Equal(HttpStatusCode.Created, series.StatusCode);
        var seriesId = (await series.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var sensor = await admin.PostAsJsonAsync("/api/sensors", new { name = "Czujnik testowy", seriesId });
        Assert.Equal(HttpStatusCode.Created, sensor.StatusCode);
        var body = await sensor.Content.ReadFromJsonAsync<JsonElement>();
        return new SensorFixture(admin, seriesId, body.GetProperty("id").GetInt32(),
            body.GetProperty("apiKey").GetString()!, sensor.Headers.Location!.ToString());
    }

    public static Task<HttpResponseMessage> SendAsync(this ApiFactory factory, string? apiKey, object body)
    {
        var client = factory.CreateApiClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/measurements") { Content = JsonContent.Create(body) };
        if (apiKey is not null)
        {
            request.Headers.Add("X-API-Key", apiKey);
        }

        return client.SendAsync(request);
    }

    public static async Task<JsonElement> SendOkAsync(this ApiFactory factory, string apiKey, object body)
    {
        var response = await factory.SendAsync(apiKey, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
