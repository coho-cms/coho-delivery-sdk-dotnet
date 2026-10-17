using System.Text.Json.Nodes;
using static CohoCms.Codegen.Tests.Fixtures;

namespace CohoCms.Codegen.Tests;

/// <summary>
/// Each fixture's generated files are compared byte for byte with the ones checked in under
/// <c>Expected/</c>, which this project also compiles against the runtime. After an intended
/// change, rewrite them with <c>COHO_UPDATE_GOLDEN=1 dotnet test</c> and review the diff.
/// </summary>
public sealed class GeneratorTests
{
    public static TheoryData<string> FixtureNames => new(Namespaces.Keys);

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void A_fixture_generates_its_expected_files_byte_for_byte(string name)
    {
        var generated = Generator.Generate(Model(name), Namespaces[name]);
        var expected = Expected(name);

        if (Environment.GetEnvironmentVariable("COHO_UPDATE_GOLDEN") == "1")
        {
            Files.Write(generated.Files, expected);
            return;
        }
        Assert.Empty(Files.Drift(generated.Files, expected));
        Assert.Empty(generated.Warnings);
    }

    [Fact]
    public void Each_type_is_its_own_file_linking_to_its_targets_by_type()
    {
        var generated = Generator.Generate(ModelOf(new()
        {
            ["post"] = [F("author", "reference", of: ["person"]), F("related", "reference", many: true, of: ["post"])],
            ["person"] = [F("name")],
        }), "Acme.Content");

        Assert.Equal(["Coho.cs", "Person.cs", "Post.cs"], generated.Files.Select(file => file.Path));
        var post = Contents(generated, "Post.cs");
        Assert.Contains("public sealed record Post : EntryBase\n{", post, StringComparison.Ordinal);
        Assert.Contains("public Linked<Person>? Author { get; init; }", post, StringComparison.Ordinal);
        Assert.Contains("Author = entry.Link<Person>(\"author\");", post, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<Linked<Post>> Related { get; init; } = [];", post, StringComparison.Ordinal);
        Assert.Contains("namespace Acme.Content;", post, StringComparison.Ordinal);
        Assert.Contains("public TypeReader<Post, NoSortOrder> Post { get; }", Contents(generated, "Coho.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_model_with_no_types_is_Coho_alone_with_only_the_untyped_reads()
    {
        var generated = Generator.Generate(ModelOf(new Dictionary<string, JsonObject[]>()), "Acme.Content");

        Assert.Equal(["Coho.cs"], generated.Files.Select(file => file.Path));
        Assert.Contains("Shape { get; } = new Dictionary<string, TypeShape>();", Contents(generated, "Coho.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Types_whose_files_would_be_one_file_on_a_case_insensitive_file_system_are_refused()
    {
        var refused = Assert.Throws<CodegenException>(() => Generator.Generate(ModelOf(new() { ["blogPost"] = [], ["blogpost"] = [] }), "Acme.Content"));

        Assert.Matches("BlogPost.cs and Blogpost.cs|Blogpost.cs and BlogPost.cs", refused.Message);
    }

    [Fact]
    public void The_order_of_types_in_the_model_does_not_change_the_output()
    {
        static string Code(Dictionary<string, JsonObject[]> types) => string.Join('\n', Generator.Generate(ModelOf(types), "N").Files.Select(file => file.Contents));

        Assert.Equal(
            Code(new() { ["post"] = [F("title")], ["author"] = [F("name")] }),
            Code(new() { ["author"] = [F("name")], ["post"] = [F("title")] }));
    }

    [Fact]
    public void A_type_named_like_one_of_the_generated_codes_own_names_is_given_Entry_after_it_with_a_warning()
    {
        var generated = Generator.Generate(ModelOf(new() { ["coho"] = [], ["linked"] = [], ["task"] = [] }), "N");

        Assert.Contains("public sealed record CohoEntry : EntryBase", Contents(generated, "CohoEntry.cs"), StringComparison.Ordinal);
        Assert.Contains("public sealed record LinkedEntry : EntryBase", Contents(generated, "LinkedEntry.cs"), StringComparison.Ordinal);
        // Task would hide System.Threading.Tasks.Task from the client's reads.
        Assert.Contains("public sealed record TaskEntry : EntryBase", Contents(generated, "TaskEntry.cs"), StringComparison.Ordinal);
        Assert.Equal(3, generated.Warnings.Count(warning => warning.Contains("is taken by the generated code", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_type_read_like_one_of_the_clients_own_members_is_read_as_its_name_and_Type_with_a_warning()
    {
        var generated = Generator.Generate(ModelOf(new() { ["source"] = [] }), "N");
        var client = Contents(generated, "Coho.cs");

        // Coho.Source would hide a record named Source in the client, so the record is SourceEntry too.
        Assert.Contains("public TypeReader<SourceEntry, NoSortOrder> SourceType { get; }", client, StringComparison.Ordinal);
        Assert.Contains("SourceType = content.Type<SourceEntry, NoSortOrder>(\"source\");", client, StringComparison.Ordinal);
        Assert.Contains(generated.Warnings, warning => warning.Contains("coho.SourceType", StringComparison.Ordinal));
    }

    [Fact]
    public void A_field_named_like_an_entrys_own_member_is_given_Field_after_it_with_a_warning()
    {
        var generated = Generator.Generate(ModelOf(new() { ["product"] = [F("id"), F("type"), F("product")] }), "N");
        var product = Contents(generated, "Product.cs");

        Assert.Contains("public string? IdField { get; init; }", product, StringComparison.Ordinal);
        Assert.Contains("IdField = entry.Text(\"id\");", product, StringComparison.Ordinal);
        Assert.Contains("public string? TypeField { get; init; }", product, StringComparison.Ordinal);
        // A member cannot share its record's name.
        Assert.Contains("public string? ProductField { get; init; }", product, StringComparison.Ordinal);
        Assert.Equal(3, generated.Warnings.Count);
    }

    [Fact]
    public void A_field_kind_this_generator_does_not_know_is_read_as_JSON_with_a_warning()
    {
        var generated = Generator.Generate(ModelOf(new() { ["post"] = [F("shape", "polygon")] }), "N");

        Assert.Contains("public JsonElement? Shape { get; init; }", Contents(generated, "Post.cs"), StringComparison.Ordinal);
        Assert.Contains("'polygon'", Assert.Single(generated.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_whose_target_type_is_not_on_the_ref_or_that_has_several_is_an_entry_of_any_type()
    {
        var generated = Generator.Generate(ModelOf(new()
        {
            ["post"] = [F("author", "reference", of: []), F("about", "reference", of: ["post", "page"])],
            ["page"] = [],
        }), "N");
        var post = Contents(generated, "Post.cs");

        Assert.Contains("public Linked<EntryBase>? Author { get; init; }", post, StringComparison.Ordinal);
        Assert.Contains("public Linked<EntryBase>? About { get; init; }", post, StringComparison.Ordinal);
    }

    [Fact]
    public void Repeater_items_are_typed_by_their_id_until_component_types_are()
    {
        var generated = Generator.Generate(ModelOf(new() { ["page"] = [F("blocks", "repeater", of: ["hero"])] }), "N");

        Assert.Contains("public IReadOnlyList<RepeaterItem> Blocks { get; init; } = [];", Contents(generated, "Page.cs"), StringComparison.Ordinal);
        Assert.Contains("Blocks = entry.Repeater(\"blocks\") ?? [];", Contents(generated, "Page.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_delivery_always_includes_is_not_nullable()
    {
        var generated = Generator.Generate(ModelOf(new()
        {
            ["post"] = [F("title", presence: "always"), F("publishedAt", "datetime", presence: "always"), F("layout", "choice", values: ["wide"], presence: "always")],
        }), "N");
        var post = Contents(generated, "Post.cs");

        Assert.Contains("public string Title { get; init; } = \"\";", post, StringComparison.Ordinal);
        Assert.Contains("Title = entry.Text(\"title\") ?? \"\";", post, StringComparison.Ordinal);
        Assert.Contains("public DateTimeOffset PublishedAt { get; init; }\n", post, StringComparison.Ordinal);
        Assert.Contains("public LayoutChoice Layout { get; init; } = new(\"\");", post, StringComparison.Ordinal);
        Assert.Contains("Always = [\"title\", \"publishedAt\", \"layout\"],", Contents(generated, "Coho.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Choice_values_are_escaped_named_where_they_can_be_and_a_display_name_cannot_break_its_doc_comment()
    {
        var generated = Generator.Generate(ModelOf(new()
        {
            ["post"] = [F("mood", "choice", name: "Mood </summary> oops", values: ["say \"hi\"", "back\\slash", "--", "2nd"])],
        }), "N");
        var post = Contents(generated, "Post.cs");

        Assert.Contains("public static MoodChoice SayHi { get; } = new(\"say \\\"hi\\\"\");", post, StringComparison.Ordinal);
        Assert.Contains("public static MoodChoice BackSlash { get; } = new(\"back\\\\slash\");", post, StringComparison.Ordinal);
        Assert.Contains("public static MoodChoice Value2nd { get; } = new(\"2nd\");", post, StringComparison.Ordinal);
        Assert.DoesNotContain("\"--\"", post, StringComparison.Ordinal);
        Assert.Contains("/// <summary>Mood &lt;/summary&gt; oops</summary>", post, StringComparison.Ordinal);
    }

    [Fact]
    public void Sortable_fields_are_the_types_sort_order()
    {
        var generated = Generator.Generate(ModelOf(new() { ["post"] = [F("rank", "integer", sortable: true), F("title")] }), "N");
        var post = Contents(generated, "Post.cs");

        Assert.Contains("public readonly record struct SortBy : ISortOrder", post, StringComparison.Ordinal);
        Assert.Contains("public static SortBy Rank { get; } = new(\"rank\");", post, StringComparison.Ordinal);
        Assert.DoesNotContain("SortBy Title", post, StringComparison.Ordinal);
        Assert.Contains("public TypeReader<Post, Post.SortBy> Post { get; }", Contents(generated, "Coho.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_model_from_a_branch_and_the_models_own_warnings_are_reported()
    {
        var warnings = new JsonArray(new JsonObject { ["type"] = "post", ["field"] = "author", ["message"] = "links to type 'ghost', which is not on this ref" });
        var generated = Generator.Generate(ModelOf(new() { ["post"] = [] }, immutable: false, @ref: "feature/x", warnings: warnings), "N");

        Assert.Equal(
            [
                "post.author: links to type 'ghost', which is not on this ref",
                "Generated from 'feature/x', a branch: its types can change. Release builds should name a tag.",
            ],
            generated.Warnings);
        Assert.Contains("ref feature/x (a branch, whose types can change)", Contents(generated, "Coho.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_model_of_another_major_version_is_refused()
    {
        var refused = Assert.Throws<CodegenException>(() => TypeModels.Read("""{ "irVersion": "2.0", "source": {}, "types": {} }""", "test"));

        Assert.Contains("version 2.0; this generator reads 1.x", refused.Message, StringComparison.Ordinal);
    }
}
