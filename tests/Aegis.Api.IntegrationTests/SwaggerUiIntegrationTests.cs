using System.Net;
using System.Text.Json;

namespace Aegis.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class SwaggerUiIntegrationTests
{
    [Fact]
    public async Task Swagger_ui_and_openapi_document_are_available_in_development()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateHttpsClient();

        var ui = await client.GetAsync("/swagger/index.html");
        var document = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains("swagger", await ui.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.True(document.IsSuccessStatusCode, await document.Content.ReadAsStringAsync());
        var openApi = await document.Content.ReadAsStringAsync();
        Assert.Contains("\"Bearer\"", openApi);
        Assert.Contains("\"scheme\": \"bearer\"", openApi);
        using var json = JsonDocument.Parse(openApi);
        var paths = json.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/auth/browser/register", "/auth/browser/login", "/auth/token/register", "/auth/token/login" })
            Assert.Contains("example.user@example.test", paths.GetProperty(path).GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example").GetProperty("email").GetString());
        foreach (var path in new[] { "/auth/token/refresh", "/auth/token/logout" })
            Assert.Equal("example-refresh-token", paths.GetProperty(path).GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example").GetProperty("refreshToken").GetString());

        var me = paths.GetProperty("/api/auth/me").GetProperty("get");
        Assert.Equal("Bearer", me.GetProperty("security")[0].EnumerateObject().Single().Name);
        Assert.False(paths.GetProperty("/auth/token/login").GetProperty("post").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task Swagger_ui_is_not_available_outside_development()
    {
        using var factory = new IntegrationTestFactory(environment: "Production");
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/index.html");
        request.Headers.Add("X-Test-Scheme", "https");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
