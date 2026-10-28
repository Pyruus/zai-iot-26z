using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Zai.Api.Tests;

[Collection(ApiCollection.Name)]
public class MeasurementsTests(ApiFactory factory)
{
    private const string T0 = "2026-01-10T10:00:00Z";
    private const string T1 = "2026-01-10T11:00:00Z";
    private const string T2 = "2026-01-10T12:00:00Z";

    private async Task<List<JsonElement>> ListAsync(string query)
    {
        var response = await factory.CreateApiClient().GetAsync($"/api/measurements?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<JsonElement>>())!;
    }

    [Fact]
    public async Task Post_StoresMeasurementForSensorSeries()
    {
        var fixture = await factory.SeriesWithSensorAsync();

        var response = await factory.SendAsync(fixture.ApiKey, new { value = 21.5, timestamp = T1 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(21.5, body.GetProperty("value").GetDouble());
        Assert.Equal(fixture.SeriesId, body.GetProperty("seriesId").GetInt32());
        Assert.Equal(fixture.SensorId, body.GetProperty("sensorId").GetInt32());
        Assert.Equal(DateTimeOffset.Parse(T1), body.GetProperty("timestamp").GetDateTimeOffset());
        var location = response.Headers.Location!.ToString();
        Assert.Equal($"/api/measurements/{body.GetProperty("id").GetInt64()}", location);
        var fetched = await factory.CreateApiClient().GetFromJsonAsync<JsonElement>(location);
        Assert.Equal(21.5, fetched.GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task Post_WithoutTimestamp_UsesServerTime()
    {
        var fixture = await factory.SeriesWithSensorAsync();

        var body = await factory.SendOkAsync(fixture.ApiKey, new { value = 5 });

        var timestamp = body.GetProperty("timestamp").GetDateTimeOffset();
        Assert.True((DateTimeOffset.UtcNow - timestamp).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Post_OutsideRange_Returns422AndIsLogged()
    {
        var fixture = await factory.SeriesWithSensorAsync(min: 0, max: 10);

        var response = await factory.SendAsync(fixture.ApiKey, new { value = 11, timestamp = T0 });

        await ProblemAssert.IsProblem(response, HttpStatusCode.UnprocessableEntity);
        Assert.Empty(await ListAsync($"series={fixture.SeriesId}"));
        var logs = await fixture.Admin.GetFromJsonAsync<List<JsonElement>>("/api/logs?kind=measurement_rejected&limit=1000");
        Assert.Contains(logs!, l => l.GetProperty("sensorId").ValueKind == JsonValueKind.Number
                                    && l.GetProperty("sensorId").GetInt32() == fixture.SensorId);
    }

    [Fact]
    public async Task Post_AcceptsBoundaryValues()
    {
        var fixture = await factory.SeriesWithSensorAsync(min: -5, max: 5);

        await factory.SendOkAsync(fixture.ApiKey, new { value = -5, timestamp = T0 });
        await factory.SendOkAsync(fixture.ApiKey, new { value = 5, timestamp = T1 });
    }

    [Fact]
    public async Task Post_WithFutureTimestamp_Returns422()
    {
        var fixture = await factory.SeriesWithSensorAsync();

        var response = await factory.SendAsync(fixture.ApiKey, new { value = 1, timestamp = DateTimeOffset.UtcNow.AddHours(1) });

        await ProblemAssert.IsProblem(response, HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("""{"value":"abc"}""")]
    [InlineData("""{}""")]
    [InlineData("""{"value":1,"timestamp":"wczoraj"}""")]
    [InlineData("""{"value":""")]
    public async Task Post_WithInvalidBody_Returns400(string json)
    {
        var fixture = await factory.SeriesWithSensorAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/measurements")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-API-Key", fixture.ApiKey);

        var response = await factory.CreateApiClient().SendAsync(request);

        await ProblemAssert.IsProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_WithTextPlain_Returns415()
    {
        var fixture = await factory.SeriesWithSensorAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/measurements")
        {
            Content = new StringContent("value=1", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-API-Key", fixture.ApiKey);

        var response = await factory.CreateApiClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutValidKey_Returns401()
    {
        await factory.SeriesWithSensorAsync();
        var adminOnly = factory.CreateApiClient();
        adminOnly.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ApiFactory.LoginAsync(adminOnly));

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.SendAsync(null, new { value = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.SendAsync(new string('x', 40), new { value = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await adminOnly.PostAsJsonAsync("/api/measurements", new { value = 1 })).StatusCode);
    }

    [Fact]
    public async Task List_FiltersSortsAndLimits()
    {
        var first = await factory.SeriesWithSensorAsync();
        var second = await factory.SeriesWithSensorAsync();
        await factory.SendOkAsync(first.ApiKey, new { value = 21.5, timestamp = T1 });
        await factory.SendOkAsync(first.ApiKey, new { value = 10, timestamp = T2 });
        await factory.SendOkAsync(first.ApiKey, new { value = 30, timestamp = T0 });
        await factory.SendOkAsync(second.ApiKey, new { value = 1, timestamp = T0 });

        var asc = await ListAsync($"series={first.SeriesId}");
        var desc = await ListAsync($"series={first.SeriesId}&sort=-timestamp");
        var range = await ListAsync($"series={first.SeriesId}&from={Uri.EscapeDataString(T0)}&to={Uri.EscapeDataString(T1)}");
        var both = await ListAsync($"series={first.SeriesId},{second.SeriesId}");
        var limited = await ListAsync($"series={first.SeriesId}&limit=2");

        Assert.Equal([30, 21.5, 10], asc.Select(m => m.GetProperty("value").GetDouble()));
        Assert.Equal([10, 21.5, 30], desc.Select(m => m.GetProperty("value").GetDouble()));
        Assert.Equal(2, range.Count);
        Assert.Equal(4, both.Count);
        Assert.Equal(2, limited.Count);
    }

    [Theory]
    [InlineData("from=nie-data")]
    [InlineData("to=2026-13-45")]
    [InlineData("series=1,abc")]
    [InlineData("sort=value")]
    [InlineData("limit=0")]
    [InlineData("limit=10001")]
    [InlineData("from=2026-01-10T12:00:00Z&to=2026-01-10T10:00:00Z")]
    public async Task List_WithInvalidParameters_Returns400(string query)
    {
        var response = await factory.CreateApiClient().GetAsync($"/api/measurements?{query}");

        await ProblemAssert.IsProblem(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Measurements_CannotBeModified(string method)
    {
        var fixture = await factory.SeriesWithSensorAsync();
        var measurement = await factory.SendOkAsync(fixture.ApiKey, new { value = 7, timestamp = T0 });
        var path = $"/api/measurements/{measurement.GetProperty("id").GetInt64()}";
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "DELETE")
        {
            request.Content = JsonContent.Create(new { value = 8, timestamp = T0 });
        }

        var response = await fixture.Admin.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var after = await factory.CreateApiClient().GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(7, after.GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task Get_Missing_Returns404()
    {
        var response = await factory.CreateApiClient().GetAsync("/api/measurements/999999999");

        await ProblemAssert.IsProblem(response, HttpStatusCode.NotFound);
    }
}
