using System.Net;

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
