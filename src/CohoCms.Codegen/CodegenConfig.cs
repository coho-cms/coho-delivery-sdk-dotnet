using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CohoCms.Codegen;

/// <summary>
/// <c>coho.config.json</c>, the same file every Coho generator reads. Paths are relative to
/// it. The token is never here: <c>pull</c> reads it from <c>COHO_TOKEN</c>, so the file can
/// be committed.
/// </summary>
internal sealed record CodegenConfig
{
    /// <summary>The Coho API's origin, such as <c>https://app.coho.example</c>. Only <c>pull</c> needs it.</summary>
    public string? Url { get; init; }

    /// <summary>The account id.</summary>
    public string? Account { get; init; }

    public string? Project { get; init; }

    /// <summary>The tag or branch to generate from. Release builds should name a tag.</summary>
    public string? Ref { get; init; }

    /// <summary>The type model file. Default <c>coho-ir.json</c>.</summary>
    public string? TypeModel { get; init; }

    /// <summary>Where generated code goes. Default <c>Coho</c>. Nothing else should be kept there.</summary>
    public string? OutDir { get; init; }

    /// <summary>
    /// The generated code's namespace. Only .NET reads it. Default: the name of the project
    /// beside the config, as <c>dotnet new</c> names its root namespace.
    /// </summary>
    public string? Namespace { get; init; }
}

/// <param name="File">The config file, resolved.</param>
/// <param name="TypeModel">The type model file, resolved.</param>
/// <param name="OutDir">The output directory, resolved.</param>
internal sealed record Loaded(CodegenConfig Config, string File, string TypeModel, string OutDir)
{
    /// <summary>The namespace the generated code is in.</summary>
    public string Namespace()
    {
        if (!string.IsNullOrWhiteSpace(Config.Namespace))
        {
            return Config.Namespace.Trim();
        }
        var projects = Directory.GetFiles(Path.GetDirectoryName(File)!, "*.csproj");
        if (projects.Length == 1)
        {
            return Path.GetFileNameWithoutExtension(projects[0]);
        }
        throw new CodegenException($"{Path.GetFileName(File)} names no namespace for the generated code, and there is not one project beside it to take it from: set \"namespace\", such as \"Acme.Content\"");
    }

    public static Loaded From(string file)
    {
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(System.IO.File.ReadAllText(file));
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new CodegenException($"Cannot read {file}: {exception.Message}");
        }
        if (json is not JsonObject)
        {
            throw new CodegenException($"{file} is not a JSON object");
        }
        CodegenConfig config;
        try
        {
            config = json.Deserialize(ConfigJson.Default.CodegenConfig)!;
        }
        catch (JsonException exception)
        {
            throw new CodegenException($"{file} is not a Coho config: {exception.Message}");
        }

        var resolved = Path.GetFullPath(file);
        var directory = Path.GetDirectoryName(resolved)!;
        return new Loaded(
            config,
            resolved,
            Path.GetFullPath(config.TypeModel ?? "coho-ir.json", directory),
            Path.GetFullPath(config.OutDir ?? "Coho", directory));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CodegenConfig))]
internal sealed partial class ConfigJson : JsonSerializerContext;
