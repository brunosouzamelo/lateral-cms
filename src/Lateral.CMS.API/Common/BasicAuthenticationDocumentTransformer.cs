using Lateral.CMS.API.Security;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Lateral.CMS.API.Common;

/// <summary>
/// Declares the Basic scheme in the OpenAPI document and applies it to the whole API, so the generated
/// reference offers a credentials box instead of answering 401 to every try.
/// </summary>
public class BasicAuthenticationDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "basic",
            Description = "User name and password of one of the configured users (see the README)."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[BasicAuthenticationDefaults.AuthenticationScheme] = scheme;

        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BasicAuthenticationDefaults.AuthenticationScheme, document)] = []
            }
        ];

        return Task.CompletedTask;
    }
}
