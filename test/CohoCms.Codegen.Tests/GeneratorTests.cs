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
        var model = Model(name);
        var generated = Generator.Generate(model, Namespaces[name]);
        if (Environment.GetEnvironmentVariable("COHO_UPDATE_GOLDEN") == "1")
        {
            Files.Write(generated.Files, ExpectedSource(name));
            return;
        }
        Assert.Empty(Files.Drift(generated.Files, Expected(name)));
        // The fixtures are tags, so the only warnings are the model's own.
        Assert.Equal(model.Warnings.Select(each => $"{each.Type}.{each.Field}: {each.Message}"), generated.Warnings);
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
    public void A_repeater_whose_components_are_not_on_the_ref_holds_untyped_items()
    {
        var generated = Generator.Generate(ModelOf(new() { ["page"] = [F("blocks", "repeater", of: [])] }), "N");

        Assert.Contains("public IReadOnlyList<RepeaterItem> Blocks { get; init; } = [];", Contents(generated, "Page.cs"), StringComparison.Ordinal);
        Assert.Contains("Blocks = entry.Repeater(\"blocks\") ?? [];", Contents(generated, "Page.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_component_type_is_an_item_record_of_its_own_and_a_repeater_of_several_holds_ItemBase()
    {
        var generated = Generator.Generate(
            ModelOf(
                new() { ["page"] = [F("blocks", "repeater", of: ["hero", "quote"]), F("refs", "repeater", of: ["quote"])] },
                components: new() { ["hero"] = [F("headline")], ["quote"] = [F("text", presence: "always")] }),
            "N");

        Assert.Equal(["Coho.cs", "Page.cs", "Hero.cs", "Quote.cs"], generated.Files.Select(file => file.Path));
        var quote = Contents(generated, "Quote.cs");
        Assert.Contains("public sealed record Quote : ItemBase\n{", quote, StringComparison.Ordinal);
        Assert.Contains("internal Quote(ItemReader item)\n        : base(item)", quote, StringComparison.Ordinal);
        Assert.Contains("Text = item.Text(\"text\") ?? \"\";", quote, StringComparison.Ordinal);

        var page = Contents(generated, "Page.cs");
        Assert.Contains("public IReadOnlyList<ItemBase> Blocks { get; init; } = [];", page, StringComparison.Ordinal);
        Assert.Contains(
            "Blocks = entry.Items<ItemBase>(\"blocks\", static each => each.Type switch { \"hero\" => new Hero(each), \"quote\" => new Quote(each), _ => null }) ?? [];",
            page,
            StringComparison.Ordinal);
        Assert.Contains("/// <summary>blocks. Each item is Hero or Quote: match on its type.</summary>", page, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<Quote> Refs { get; init; } = [];", page, StringComparison.Ordinal);

        // Exported, but not entries: no reader, and nothing to hydrate.
        var client = Contents(generated, "Coho.cs");
        Assert.DoesNotContain("Quote", client, StringComparison.Ordinal);
    }

    [Fact]
    public void A_component_cannot_share_a_name_with_a_content_type()
    {
        var refused = Assert.Throws<CodegenException>(() => Generator.Generate(ModelOf(new() { ["quote"] = [] }, components: new() { ["quote"] = [] }), "N"));

        Assert.Contains("both be named Quote", refused.Message, StringComparison.Ordinal);
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
    public void A_type_that_extends_another_derives_from_its_record_and_declares_only_what_it_adds()
    {
        var generated = Generator.Generate(Model("inheritance"), "N");

        Assert.Contains("public abstract record Page : EntryBase\n{\n    protected Page()", Contents(generated, "Page.cs"), StringComparison.Ordinal);
        var service = Contents(generated, "ServicePage.cs");
        // Not sealed: a premium service is one.
        Assert.Contains("public record ServicePage : Page\n{", service, StringComparison.Ordinal);
        // What it inherits is Page's to declare and read, tightened or not.
        Assert.DoesNotContain("public string? Title {", service, StringComparison.Ordinal);
        Assert.DoesNotContain("public long? Rank {", service, StringComparison.Ordinal);
        Assert.Contains("Tightens Title and Summary, which keep Page's types.", service, StringComparison.Ordinal);
        Assert.Contains("public new readonly record struct SortBy : ISortOrder", service, StringComparison.Ordinal);
        Assert.Contains("public sealed record PremiumService : ServicePage\n{", Contents(generated, "PremiumService.cs"), StringComparison.Ordinal);

        // A link to a base type is a record of any type in its family.
        Assert.Contains("public Linked<ServicePage>? Featured { get; init; }", Contents(generated, "Menu.cs"), StringComparison.Ordinal);
        // A repeater of a base component builds each concrete one.
        Assert.Contains(
            "Blocks = entry.Items<Block>(\"blocks\", static each => each.Type switch { \"quoteBlock\" => new QuoteBlock(each), \"textBlock\" => new TextBlock(each), _ => null }) ?? [];",
            Contents(generated, "Page.cs"),
            StringComparison.Ordinal);

        var client = Contents(generated, "Coho.cs");
        // A read of one entry keeps any type in the family, as delivery's lists do.
        Assert.Contains("Page = content.Type<Page, Page.SortBy>(\"page\", [\"landingPage\", \"premiumService\", \"servicePage\"]);", client, StringComparison.Ordinal);
        Assert.Contains("LandingPage = content.Type<LandingPage, LandingPage.SortBy>(\"landingPage\");", client, StringComparison.Ordinal);
        // An abstract type has no entries to hydrate.
        Assert.DoesNotContain("[\"page\"] = new TypeShape", client, StringComparison.Ordinal);
    }

    [Fact]
    public void An_abstract_type_with_no_concrete_descendants_reads_only_its_own_slug()
    {
        var generated = Generator.Generate(ModelOf(new() { ["page"] = [], ["post"] = [] }, extends: new() { ["post"] = "page" }, @abstract: ["page", "post"]), "N");

        Assert.Contains("public abstract record Post : Page\n{", Contents(generated, "Post.cs"), StringComparison.Ordinal);
        Assert.Contains("Page = content.Type<Page, NoSortOrder>(\"page\");", Contents(generated, "Coho.cs"), StringComparison.Ordinal);
        Assert.Contains("Shape { get; } = new Dictionary<string, TypeShape>();", Contents(generated, "Coho.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_that_extends_itself_is_refused()
    {
        var refused = Assert.Throws<CodegenException>(() => Generator.Generate(ModelOf(new() { ["a"] = [], ["b"] = [] }, extends: new() { ["a"] = "b", ["b"] = "a" }), "N"));

        Assert.Contains("extends itself", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trait_is_an_interface_each_record_that_takes_it_implements()
    {
        var generated = Generator.Generate(Model("traits"), "N");

        Assert.Contains("public interface ISeo\n{", Contents(generated, "ISeo.cs"), StringComparison.Ordinal);
        Assert.Contains("public record Page : EntryBase, ISeo\n{", Contents(generated, "Page.cs"), StringComparison.Ordinal);
        var article = Contents(generated, "Article.cs");
        // Its parent's record implements the parent's traits; it implements its own.
        Assert.Contains("public sealed record Article : Page, IPublishing, ISocialCard\n{", article, StringComparison.Ordinal);
        // What it has otherwise than the trait does, it gives as the trait has it.
        Assert.Contains("string? IPublishing.Status => Status.Value;", article, StringComparison.Ordinal);
        Assert.Contains("long? IPublishing.Priority => Priority;", article, StringComparison.Ordinal);
        Assert.DoesNotContain("IPublishing.PublishedAt", article, StringComparison.Ordinal);
        // A trait is not a type: no reader, nothing to hydrate.
        Assert.DoesNotContain("Seo", Contents(generated, "Coho.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Code_written_against_a_trait_takes_a_record_of_any_type_with_it()
    {
        // The generated records, compiled into this project.
        var article = new Golden.Traits.Article { Id = "a", Type = "article", Slug = "a", Priority = 2 };
        var person = new Golden.Traits.Person { Id = "p", Type = "person", Slug = "p" };
        List<Golden.Traits.ISocialCard> shared = [article, person];
        Golden.Traits.IPublishing published = article;

        Assert.Equal(2, shared.Count);
        Assert.Equal(2, published.Priority);
    }

    [Fact]
    public void A_type_model_of_another_major_version_is_refused()
    {
        var refused = Assert.Throws<CodegenException>(() => TypeModels.Read("""{ "irVersion": "2.0", "source": {}, "types": {} }""", "test"));

        Assert.Contains("version 2.0; this generator reads 1.x", refused.Message, StringComparison.Ordinal);
    }
}
