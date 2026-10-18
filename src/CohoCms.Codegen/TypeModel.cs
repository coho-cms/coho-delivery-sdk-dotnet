using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CohoCms.Codegen;

// The type model: a language-neutral description of one ref's content types, produced
// by the Coho server. See `type-model.schema.json` in coho-sdk-spec.
//
// Only what the generator reads is declared here. Readers ignore keys they do not know,
// which is what lets a minor version add them. A property with a default is settable:
// deserialization sets an init-only property it does not find to null, past its default.

internal sealed record TypeModel
{
    public required string IrVersion { get; init; }

    public required Source Source { get; init; }

    /// <summary>Content types by slug.</summary>
    public required Dictionary<string, ContentType> Types { get; init; }

    public List<ModelWarning> Warnings { get; set; } = [];
}

internal sealed record Source
{
    public required string Project { get; init; }

    /// <summary>The branch or tag the types came from.</summary>
    public required string Ref { get; init; }

    /// <summary>True for a tag, whose types can never change.</summary>
    public bool Immutable { get; set; }

    public required string SchemaHash { get; init; }
}

internal sealed record ContentType
{
    public required string Slug { get; init; }

    public required string Name { get; init; }

    public string Version { get; set; } = "";

    public required List<Field> Fields { get; init; }
}

internal sealed record Field
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Open: a kind added after this generator was written is emitted untyped.</summary>
    public required string Kind { get; init; }

    /// <summary>Whether delivery always includes the field: <c>always</c> or <c>maybe</c>. The one rule for optionality.</summary>
    public string Presence { get; set; } = "maybe";

    public bool Required { get; set; }

    public bool Localized { get; set; }

    public bool Sortable { get; set; }

    public bool Many { get; set; }

    /// <summary>Reference targets, each a key of <c>types</c>; or a repeater's component types.</summary>
    public List<string> Of { get; set; } = [];

    public List<string> Values { get; set; } = [];

    /// <summary>On assets (IR 1.1): the media types a linked file may be, or family wildcards. Empty for any.</summary>
    public List<string> Accept { get; set; } = [];

    public string? TextFormat { get; set; }

    public bool Always => Presence == "always";
}

internal sealed record ModelWarning(string Type, string Field, string Message);

/// <summary>A type model, a configuration or a server answer the generator cannot use.</summary>
internal sealed class CodegenException(string message) : Exception(message);

internal static partial class TypeModels
{
    /// <summary>The type model's major version this generator reads.</summary>
    public const int SupportedMajor = 1;

    /// <summary>
    /// Checks that <paramref name="json"/> is a type model this generator can read, and returns
    /// it. An unknown major version is refused: the shape may mean something this generator
    /// cannot know.
    /// </summary>
    public static TypeModel Read(string json, string from)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new CodegenException($"{from} is not JSON");
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new CodegenException("The type model is not a JSON object");
        }

        var version = root.TryGetProperty("irVersion", out var irVersion) && irVersion.ValueKind == JsonValueKind.String ? irVersion.GetString()! : null;
        if (version is null || !VersionPattern().IsMatch(version))
        {
            throw new CodegenException($"The type model has no valid irVersion: {(version is null ? "none" : $"\"{version}\"")}");
        }
        var major = int.Parse(version.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture);
        if (major != SupportedMajor)
        {
            throw new CodegenException($"The type model is version {version}; this generator reads {SupportedMajor}.x. Upgrade CohoCms.Codegen.");
        }

        try
        {
            return root.Deserialize(TypeModelJson.Default.TypeModel) ?? throw new CodegenException("The type model is null");
        }
        catch (JsonException exception)
        {
            throw new CodegenException($"The type model does not have the shape this generator reads: {exception.Message}");
        }
    }

    [GeneratedRegex(@"^\d+\.\d+$")]
    private static partial Regex VersionPattern();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TypeModel))]
internal sealed partial class TypeModelJson : JsonSerializerContext;
