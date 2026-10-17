using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CohoCms.Codegen;

/// <summary>What a project token says about itself: Coho's <c>GET /api/v1/token</c>.</summary>
internal sealed record TokenDescription(TokenDescription.Named Account, TokenDescription.Named Project, TokenDescription.About Token)
{
    internal sealed record Named(string Id, string? Name);

    internal sealed record About(string Id, string Label, string Role);
}

/// <summary>
/// Points a library made from the template at a Coho project: what <c>create-coho</c> does
/// for a TypeScript library after copying its template, which <c>dotnet new</c> does here.
/// </summary>
internal static class Init
{
    /// <summary>How a project token announces itself.</summary>
    public const string TokenPrefix = "coho_pt_";

    /// <summary>
    /// Asks Coho at <paramref name="url"/> which account and project <paramref name="token"/> is
    /// for. A token names its own project, so nothing else is needed to find them: no account
    /// or project ids to copy.
    /// </summary>
    public static async Task<TokenDescription> DescribeTokenAsync(string url, string token, HttpMessageInvoker http, CancellationToken cancellationToken)
    {
        if (!url.StartsWith("http://", StringComparison.Ordinal) && !url.StartsWith("https://", StringComparison.Ordinal))
        {
            throw new CodegenException($"'{url}' is not a URL, such as https://app.coho.example");
        }
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            throw new CodegenException($"A project token starts {TokenPrefix}. Create one with: coho token create --role viewer");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{url.TrimEnd('/')}/api/v1/token");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Files.Send(http, request, url, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize(body, InitJson.Default.TokenDescription) ?? throw new CodegenException("Coho described the token as null");
            }
            catch (JsonException exception)
            {
                throw new CodegenException($"Coho's description of the token is not what this tool reads: {exception.Message}");
            }
        }

        var failure = await Files.FailureAsync(response, "Asking Coho about the token", cancellationToken).ConfigureAwait(false);
        var hint = (int)response.StatusCode switch
        {
            401 => " Check that it is a current project token, not revoked or expired.",
            403 => " The account is suspended or closed.",
            404 => " Check the URL: it is Coho's, as `coho status` shows it, not delivery's.",
            _ => "",
        };
        throw new CodegenException($"Coho refused the token. {failure.Replace(" Check COHO_TOKEN: it needs read access to the project.", "", StringComparison.Ordinal)}{hint}");
    }

    /// <summary>Sets <paramref name="values"/> in the config file, keeping everything else in it as it was.</summary>
    public static void EditConfig(string file, IReadOnlyDictionary<string, string?> values)
    {
        var json = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject : new JsonObject();
        if (json is null)
        {
            throw new CodegenException($"{file} is not a JSON object");
        }
        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                json[key] = value;
            }
        }
        File.WriteAllText(file, json.ToJsonString(Indented) + "\n");
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, IndentSize = 2, TypeInfoResolver = InitJson.Default };
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TokenDescription))]
[JsonSerializable(typeof(JsonObject))]
internal sealed partial class InitJson : JsonSerializerContext;
