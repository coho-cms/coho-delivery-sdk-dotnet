using CohoCms.Delivery;
using CohoCms.Delivery.Tests;
using Golden.DeliveryFixture;
using Microsoft.Extensions.DependencyInjection;

namespace CohoCms.Codegen.Tests;

/// <summary>
/// The generated code, run. <c>Expected/delivery-fixture</c> is what the generator writes for
/// the types of the delivery recordings' content, and here it reads those recordings through
/// the runtime, as a customer's library reads the server.
/// </summary>
public sealed class IntegrationTests
{
    private static readonly CancellationToken None = TestContext.Current.CancellationToken;

    /// <summary>A client over the recordings, in <paramref name="locale"/>.</summary>
    private static Coho Client(string? locale = "en-US")
    {
        return new Coho(
            new ContentConfig { Delivery = new MountedConfig { BaseUrl = Spec.Base, Key = Spec.Key }, Ref = "v0.0.1", Locale = locale },
            Spec.Options(new Replay()));
    }

    [Fact]
    public async Task A_typed_read_is_hydrated_through_the_generated_client()
    {
        var chain = await Client().Article.BySlugAsync("chain-1", new ReadOptions { Depth = 2 }, None);

        Assert.Equal("article", chain?.Type);
        Assert.Equal("Chain 1", chain?.Title);
        var next = Assert.Single(chain!.Related);
        Assert.True(next.IsResolved);
        Assert.Equal("Chain 2", next.Value.Title);
    }

    [Fact]
    public async Task A_typed_list_is_ordered_by_a_sortable_field()
    {
        var page = await Client().Article.ListAsync(new TypeListOptions<Article.SortBy> { Order = Article.SortBy.Rank.Descending, Limit = 3 }, None);

        Assert.Equal([103L, 102L, 101L], page.Items.Select(article => article.Rank));
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task A_type_with_no_entries_lists_as_an_empty_page()
    {
        // The recording asked without a locale.
        var page = await Client(locale: null).Notice.ListAsync(new TypeListOptions<Notice.SortBy> { Order = Notice.SortBy.Priority.Descending }, None);

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task The_untyped_reads_return_any_type_narrowed_by_matching()
    {
        var entry = await Client().BySlugAsync("chain-1", new ReadOptions { Depth = 1 }, None);

        var article = Assert.IsType<Article>(entry);
        Assert.Equal(101L, article.Rank);
    }

    [Fact]
    public void The_client_names_the_type_model_it_was_generated_from()
    {
        Assert.Equal("v0.0.1", Coho.Source.Ref);
        Assert.True(Coho.Source.Immutable);
    }

    [Fact]
    public async Task AddCoho_registers_the_client_for_an_app()
    {
        var replay = new Replay();
        var services = new ServiceCollection();
        services.AddCoho(_ => new ContentConfig { Delivery = new MountedConfig { BaseUrl = Spec.Base, Key = Spec.Key }, Ref = "v0.0.1", Locale = "en-US" })
            .ConfigurePrimaryHttpMessageHandler(() => replay);

        using var provider = services.BuildServiceProvider();
        var chain = await provider.GetRequiredService<Coho>().Article.BySlugAsync("chain-1", new ReadOptions { Depth = 2 }, None);

        Assert.Equal("Chain 1", chain?.Title);
    }
}
