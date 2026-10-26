using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Zai.Api.Tests;

[Collection(ApiCollection.Name)]
public class PipelineTests(ApiFactory factory)
{
    [Fact]
    public async Task UnsupportedAccept_Returns406()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
    }

    [Theory]
    [InlineData("*/*")]
    [InlineData("application/json")]
    [InlineData("application/*")]
    public async Task CompatibleAccept_IsServed(string accept)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Accept.ParseAdd(accept);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiPath_ReturnsProblem404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal(404, Convert.ToInt32(problem!["status"].ToString()));
        Assert.True(problem.ContainsKey("title"));
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/does-not-exist")]
    public async Task Responses_HaveSecurityHeaders(string path)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
}
