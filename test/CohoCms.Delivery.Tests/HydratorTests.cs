using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CohoCms.Delivery.Tests;

/// <summary>What generated code would declare for the recordings' <c>article</c> type.</summary>
internal sealed record Article : EntryBase
{
    public Article()
    {
    }

    [SetsRequiredMembers]
    private Article(EntryReader entry)
        : base(entry)
    {
        Title = entry.Text("title");
        Body = entry.Text("body");
        Rank = entry.Integer("rank");
        Related = entry.Links<Article>("related");
    }

    public string? Title { get; init; }

    public string? Body { get; init; }

    public long? Rank { get; init; }

    public IReadOnlyList<Linked<Article>>? Related { get; init; }

    public static readonly TypeShape Shape = new()
    {
        Links = ImmutableDictionary.CreateRange([KeyValuePair.Create("related", LinkKind.Many)]),
        Read = entry => new Article(entry),
    };
}

public sealed class HydratorTests
{
    private const string C1 = "00000000-0000-7000-8000-0000000000c1";
    private const string C2 = "00000000-0000-7000-8000-0000000000c2";
    private const string Absent = "00000000-0000-7000-8000-0000000000ff";

    private static readonly Dictionary<string, TypeShape> Untyped = new()
    {
        ["article"] = new TypeShape { Links = ImmutableDictionary.CreateRange([KeyValuePair.Create("related", LinkKind.Many)]) },
    };

    private static readonly Dictionary<string, TypeShape> Typed = new() { ["article"] = Article.Shape };

    private static Entry Entry(string id, string type, string fields, string? locale = null)
    {
        var parsed = JsonDocument.Parse(fields).RootElement.EnumerateObject().ToImmutableDictionary(property => property.Name, property => property.Value.Clone());
        return new Entry { Id = id, Slug = id, Type = type, Locale = locale, Fields = parsed };
    }

    private static EntryGraph One(Entry entry, ImmutableDictionary<string, string>? unresolved = null)
    {
        return new EntryGraph
        {
            Roots = [entry.Id],
            Entries = ImmutableDictionary.CreateRange([KeyValuePair.Create(entry.Id, entry)]),
            Unresolved = unresolved ?? ImmutableDictionary<string, string>.Empty,
        };
    }

    [Fact]
    public void A_depth_2_read_is_a_tree_links_resolve_the_dangling_one_is_absent_and_the_way_back_to_the_root_is_a_cycle()
    {
        var root = (Article)Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-by-slug-depth-2"), Typed));

        Assert.Equal("chain-1", root.Slug);
        var two = root.Related![0].Value!;
        Assert.Equal("chain-2", two.Slug);
        var three = two.Related![0].Value!;
        Assert.Equal("chain-3", three.Slug);
        Assert.Equal((Absent, UnresolvedReason.Absent), (two.Related[1].Id, two.Related[1].Unresolved));
        var back = Assert.Single(three.Related!);
        Assert.Equal((C1, UnresolvedReason.Cycle), (back.Id, back.Unresolved));
        Assert.False(back.IsResolved);
    }

    [Fact]
    public void A_link_beyond_the_requested_depth_is_a_depth_stub()
    {
        var root = (Article)Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-by-slug"), Typed));

        var link = Assert.Single(root.Related!);
        Assert.Equal((C2, UnresolvedReason.Depth), (link.Id, link.Unresolved));
    }

    [Fact]
    public void An_untyped_shape_follows_links_beside_the_fields_as_sent()
    {
        var root = (HydratedEntry)Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-by-slug-depth-2"), Untyped));

        Assert.Equal("Chain 1", root.Field("title")?.GetString());
        Assert.Equal(C2, root.Field("related")?[0].GetString());
        var two = Assert.Single(root.Links("related")!);
        Assert.True(two.TryGetValue(out var target));
        Assert.Equal("chain-2", target.Slug);
    }

    [Fact]
    public void Without_a_shape_references_stay_ids()
    {
        var root = (HydratedEntry)Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-by-slug-depth-2")));

        Assert.Null(root.Links("related"));
        Assert.Equal(C2, root.Field("related")?[0].GetString());
    }

    [Fact]
    public void System_values_sit_beside_the_fields_and_a_field_named_like_one_is_untouched()
    {
        var root = (HydratedEntry)Assert.Single(Hydrator.Hydrate(One(Entry("e1", "product", """{ "id": "SKU-1", "type": "widget", "slug": "x", "locale": "fr" }""", "en-US"))));

        Assert.Equal(("e1", "product", "e1", "en-US"), (root.Id, root.Type, root.Slug, root.Locale));
        Assert.Equal("SKU-1", root.Field("id")?.GetString());
        Assert.Equal("widget", root.Field("type")?.GetString());
    }

    [Fact]
    public void Each_root_has_its_own_tree_so_a_root_met_on_another_roots_path_is_not_cut_short()
    {
        // The page is chain-3, chain-2, chain-1, each linking to the next round the loop.
        var items = Hydrator.Hydrate(Spec.Graph("entries-ordered"), Typed).Cast<Article>().ToList();
        Assert.Equal(["chain-3", "chain-2", "chain-1"], items.Select(item => item.Slug));

        // chain-2 is on chain-3's path, and still resolves chain-3 in its own tree.
        var fromTwo = items[1].Related![0];
        Assert.True(fromTwo.IsResolved);
        Assert.Equal("chain-3", fromTwo.Value.Slug);
    }

    [Fact]
    public void A_field_the_type_always_has_missing_from_an_entry_is_reported_once_per_entry_and_the_entry_is_still_returned()
    {
        var issues = new List<Issue>();
        var strict = new Dictionary<string, TypeShape> { ["article"] = Article.Shape with { Always = ["body"] } };

        var items = Hydrator.Hydrate(Spec.Graph("entries-ordered"), strict, issues.Add);

        Assert.Equal(3, items.Count);
        // Every chain entry lacks `body`, and each appears in all three trees.
        Assert.Equal(
            ["MissingField chain-1 body", "MissingField chain-2 body", "MissingField chain-3 body"],
            issues.Select(issue => $"{issue.Kind} {issue.Slug} {issue.Field}").Order(StringComparer.Ordinal));
        Assert.Contains("has no 'body'", issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_delivery_neither_returned_nor_reported_is_reported_and_becomes_an_absent_stub()
    {
        var issues = new List<Issue>();

        var root = (Article)Assert.Single(Hydrator.Hydrate(One(Entry("a", "article", """{ "related": ["zzz"] }""")), Typed, issues.Add));

        var link = Assert.Single(root.Related!);
        Assert.Equal(("zzz", UnresolvedReason.Absent), (link.Id, link.Unresolved));
        Assert.Equal([IssueKind.UnlistedLink], issues.Select(issue => issue.Kind));
    }

    [Fact]
    public void One_link_where_the_type_has_many_is_reported_and_read_as_none()
    {
        var issues = new List<Issue>();
        var unresolved = ImmutableDictionary.CreateRange([KeyValuePair.Create(C2, UnresolvedReason.Depth)]);

        var root = (Article)Assert.Single(Hydrator.Hydrate(One(Entry(C1, "article", $$"""{ "related": "{{C2}}" }"""), unresolved), Typed, issues.Add));

        Assert.Null(root.Related);
        Assert.Equal([IssueKind.UnexpectedValue], issues.Select(issue => issue.Kind));
    }

    [Fact]
    public void A_value_of_the_wrong_kind_is_reported_and_read_as_none()
    {
        var issues = new List<Issue>();

        var root = (Article)Assert.Single(Hydrator.Hydrate(One(Entry("a", "article", """{ "title": 7, "rank": "first" }""")), Typed, issues.Add));

        Assert.Null(root.Title);
        Assert.Null(root.Rank);
        Assert.Equal(["rank", "title"], issues.Select(issue => issue.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_link_to_an_entry_of_another_type_is_reported_and_becomes_a_type_stub()
    {
        var issues = new List<Issue>();
        var page = Entry("p", "page", "{}");
        var article = Entry("a", "article", """{ "related": ["p"] }""");
        var graph = new EntryGraph
        {
            Roots = ["a"],
            Entries = ImmutableDictionary.CreateRange([KeyValuePair.Create("a", article), KeyValuePair.Create("p", page)]),
        };

        var root = (Article)Assert.Single(Hydrator.Hydrate(graph, Typed, issues.Add));

        Assert.Equal(UnresolvedReason.WrongType, Assert.Single(root.Related!).Unresolved);
        Assert.Equal([IssueKind.UnexpectedValue], issues.Select(issue => issue.Kind));
    }

    [Fact]
    public void A_null_link_stays_null_and_is_not_reported()
    {
        var issues = new List<Issue>();

        var root = (Article)Assert.Single(Hydrator.Hydrate(One(Entry("a", "article", """{ "related": null }""")), Typed, issues.Add));

        Assert.Null(root.Related);
        Assert.Empty(issues);
    }

    [Fact]
    public void A_hydrated_tree_serializes_as_it_is()
    {
        var root = Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-by-slug-depth-2"), Typed));

        // No cycles, so nothing to refuse. Reflection here; a generated library's context is its own.
#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize<object>(root);
#pragma warning restore IL2026, IL3050

        Assert.Contains("\"Slug\":\"chain-3\"", json, StringComparison.Ordinal);
    }
}
