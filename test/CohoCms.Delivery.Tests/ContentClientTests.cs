namespace CohoCms.Delivery.Tests;

/// <summary>What generated code would declare for <see cref="Article"/>'s sortable fields.</summary>
internal readonly record struct ArticleOrder : ISortOrder
{
    private ArticleOrder(string value)
    {
        Value = value;
    }

    public string? Value { get; }

    public static ArticleOrder Rank => new("rank");

    public ArticleOrder Descending => new($"-{Value?.TrimStart('-')}");
}

public sealed class ContentClientTests
{
    private const string C2 = "00000000-0000-7000-8000-0000000000c2";
    private const string C3 = "00000000-0000-7000-8000-0000000000c3";
    private const string Absent = "00000000-0000-7000-8000-0000000000ff";

    private static readonly CancellationToken None = TestContext.Current.CancellationToken;

    private static (ContentClient Client, Replay Replay, TypeReader<Article, ArticleOrder> Articles) Content(
        TypeShape? article = null,
        Action<Issue>? onIssue = null,
        string? locale = "en-US")
    {
        var replay = new Replay();
        var client = new ContentClient(
            new ContentConfig { Delivery = new MountedConfig { BaseUrl = Spec.Base, Key = Spec.Key }, Ref = "v0.0.1", Locale = locale },
            new Dictionary<string, TypeShape> { ["article"] = article ?? Article.Shape },
            Spec.Options(replay) with { OnIssue = onIssue });
        return (client, replay, client.Type<Article, ArticleOrder>("article"));
    }

    [Fact]
    public async Task A_typed_read_is_hydrated()
    {
        var (_, _, articles) = Content();

        var chain = await articles.BySlugAsync("chain-1", new ReadOptions { Depth = 2 }, None);

        Assert.Equal("Chain 1", chain?.Title);
        var next = Assert.IsType<Linked<Article>>(chain?.Related?[0]);
        Assert.True(next.IsResolved);
        Assert.Equal("Chain 2", next.Value.Title);
    }

    [Fact]
    public async Task A_slug_of_another_type_is_null_the_same_as_no_entry()
    {
        var (client, _, _) = Content();
        Assert.Null(await client.Type<HydratedEntry>("page").BySlugAsync("chain-1", new ReadOptions { Depth = 2 }, None));

        // The recording asked without a locale.
        var (bare, _, _) = Content(locale: null);
        Assert.Null(await bare.Type<Article>("article").BySlugAsync("nope", cancellationToken: None));
    }

    [Fact]
    public async Task A_read_of_a_base_type_finds_an_entry_of_any_type_in_its_family()
    {
        var (client, _, _) = Content();

        // As generated code reads a base type that `article` extends.
        var chain = await client.Type<Article>("page", ["article", "landing"]).BySlugAsync("chain-1", new ReadOptions { Depth = 2 }, None);

        Assert.Equal("Chain 1", chain?.Title);
        Assert.Null(await client.Type<Article>("page", ["landing"]).BySlugAsync("chain-1", new ReadOptions { Depth = 2 }, None));
    }

    [Fact]
    public async Task The_configured_locale_is_sent_unless_a_read_names_its_own()
    {
        var (_, replay, articles) = Content();

        var chain = await articles.ByIdAsync(C3, new ReadOptions { Depth = 1, Locale = "de-DE" }, None);

        Assert.Equal("Kette 3", chain?.Title);
        Assert.Contains("locale=de-DE", replay.Calls[0].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_list_keeps_the_page_order_and_HasMore_and_hydrates_each_item()
    {
        var (_, _, articles) = Content();

        var page = await articles.ListAsync(new TypeListOptions<ArticleOrder> { Order = ArticleOrder.Rank.Descending, Limit = 3 }, None);

        Assert.Equal(["chain-3", "chain-2", "chain-1"], page.Items.Select(item => item.Slug));
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task ByIds_leaves_out_the_ids_that_are_not_on_the_ref()
    {
        var (client, _, _) = Content();

        var found = await client.ByIdsAsync([C2, Absent], cancellationToken: None);

        Assert.Equal(["chain-2"], found.Select(entry => entry.Slug));
    }

    [Fact]
    public async Task An_untyped_read_returns_the_declared_type_when_there_is_one()
    {
        var (client, _, _) = Content();

        var entry = await client.BySlugAsync("chain-1", cancellationToken: None);

        Assert.IsType<Article>(entry);
    }

    [Fact]
    public async Task An_issue_is_reported_once_for_the_life_of_the_client_not_once_per_read()
    {
        var issues = new List<Issue>();
        var (_, _, articles) = Content(Article.Shape with { Always = ["body"] }, issues.Add);

        await articles.ListAsync(new TypeListOptions<ArticleOrder> { Order = ArticleOrder.Rank.Descending, Limit = 3 }, None);
        await articles.ListAsync(new TypeListOptions<ArticleOrder> { Order = ArticleOrder.Rank.Descending, Limit = 3 }, None);

        Assert.Equal(["chain-1", "chain-2", "chain-3"], issues.Select(issue => issue.Slug).Order(StringComparer.Ordinal));
    }
}
