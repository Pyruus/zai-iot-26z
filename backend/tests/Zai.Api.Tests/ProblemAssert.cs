using System.Net;
using System.Text.Json;

namespace Zai.Api.Tests;

public static class ProblemAssert
{
    public static async Task IsProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("title").ValueKind);
    }
}
