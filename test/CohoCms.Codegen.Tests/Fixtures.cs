using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CohoCms.Codegen.Tests;

internal static class Fixtures
{
    /// <summary>The type model fixtures, each generated into <c>Expected/</c> under its own namespace.</summary>
    public static readonly Dictionary<string, string> Namespaces = new()
    {
        ["cli-next-blog"] = "Golden.CliNextBlog",
        ["delivery-fixture"] = "Golden.DeliveryFixture",
        ["assets"] = "Golden.Assets",
        ["repeaters"] = "Golden.Repeaters",
    };

    public static string ModelFile(string name) => Path.Combine(AppContext.BaseDirectory, "spec", "type-model", "fixtures", $"{name}.json");

    public static TypeModel Model(string name) => TypeModels.Read(File.ReadAllText(ModelFile(name)), name);

    /// <summary>A fixture's expected files, as copied beside the tests.</summary>
    public static string Expected(string name) => Path.Combine(AppContext.BaseDirectory, "Expected", name);

    /// <summary>
    /// A fixture's expected files in the source tree, where a golden update writes. Not the
    /// compiler's path for this file: a CI build maps source paths to <c>/_/</c>.
    /// </summary>
    public static string ExpectedSource(string name)
    {
        var project = typeof(Fixtures).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(each => each.Key == "ProjectDirectory").Value!;
        return Path.Combine(project, "Expected", name);
    }

    public static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coho-codegen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A type model with the given types, each field filled out with the defaults a plain optional field has.</summary>
    public static TypeModel ModelOf(
        Dictionary<string, JsonObject[]> types,
        bool immutable = true,
        string @ref = "v1.0.0",
        JsonArray? warnings = null,
        Dictionary<string, JsonObject[]>? components = null)
    {
        static JsonObject Types(Dictionary<string, JsonObject[]> types)
        {
            return new JsonObject(types.Select(type => KeyValuePair.Create<string, JsonNode?>(type.Key, new JsonObject
            {
                ["slug"] = type.Key,
                ["name"] = type.Key,
                ["version"] = "v",
                ["fields"] = new JsonArray(type.Value.Select(Field).ToArray()),
            })));
        }

        var json = new JsonObject
        {
            ["irVersion"] = components is null ? "1.0" : "1.2",
            ["source"] = new JsonObject { ["project"] = "p1", ["ref"] = @ref, ["immutable"] = immutable, ["schemaHash"] = "sha256:0" },
            ["types"] = Types(types),
        };
        if (components is not null)
        {
            json["components"] = Types(components);
        }
        if (warnings is not null)
        {
            json["warnings"] = warnings;
        }
        return TypeModels.Read(json.ToJsonString(), "test");
    }

    /// <summary>A field: <paramref name="field"/>'s keys over a plain optional text field's.</summary>
    private static JsonNode? Field(JsonObject field)
    {
        var filled = new JsonObject
        {
            ["name"] = field["id"]?.DeepClone(),
            ["kind"] = "text",
            ["presence"] = "maybe",
            ["required"] = false,
            ["localized"] = false,
            ["sortable"] = false,
        };
        foreach (var (key, value) in field)
        {
            filled[key] = value?.DeepClone();
        }
        return filled;
    }

    public static JsonObject F(string id, string kind = "text", bool many = false, string[]? of = null, string? name = null, string[]? values = null, bool sortable = false, string presence = "maybe")
    {
        var field = new JsonObject { ["id"] = id, ["kind"] = kind, ["many"] = many, ["sortable"] = sortable, ["presence"] = presence };
        if (of is not null)
        {
            field["of"] = new JsonArray(of.Select(slug => (JsonNode?)slug).ToArray());
        }
        if (name is not null)
        {
            field["name"] = name;
        }
        if (values is not null)
        {
            field["values"] = new JsonArray(values.Select(value => (JsonNode?)value).ToArray());
        }
        return field;
    }

    public static string Contents(Generated generated, string path)
    {
        var found = generated.Files.FirstOrDefault(file => file.Path == path);
        Assert.True(found is not null, $"{path} was not generated");
        return found.Contents;
    }

    public static string Serialize(object value) => JsonSerializer.Serialize(value);
}
