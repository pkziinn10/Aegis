using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aegis.Api.OpenApi;

public sealed class AuthenticationExamplesOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var path = context.Description.RelativePath?.Trim('/') ?? string.Empty;
        JsonNode? example = path switch
        {
            "auth/browser/register" or "auth/browser/login" or
            "auth/token/register" or "auth/token/login" =>
                JsonNode.Parse("{\"email\":\"example.user@example.test\",\"password\":\"example-password-123\"}"),
            "auth/token/refresh" or "auth/token/logout" =>
                JsonNode.Parse("{\"refreshToken\":\"example-refresh-token\"}"),
            _ => null
        };

        if (example is not null && operation.RequestBody?.Content is { } content)
            foreach (var mediaType in content.Values)
                mediaType.Example = example.DeepClone();

        return Task.CompletedTask;
    }
}
