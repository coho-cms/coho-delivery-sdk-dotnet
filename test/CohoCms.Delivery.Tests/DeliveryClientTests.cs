using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;

namespace CohoCms.Delivery.Tests;

public sealed class DeliveryClientTests
{
    private static readonly CancellationToken None = TestContext.Current.CancellationToken;

    private static DeliveryClient Mounted(HttpMessageHandler handler, string? key = null, TimeProvider? time = null, int maxCachedResponses = 1000)
    {
        return new DeliveryClient(new MountedConfig { BaseUrl = Spec.Base, Key = key }, Spec.Options(handler, time, maxCachedResponses));
    }

    /* REQUESTS */
    /* REQUESTS */
    /* REQUESTS */

    [Fact]
    public async Task Requests_go_to_the_contracts_paths_with_the_key_in_a_header_and_the_parameters_in_name_order()
    {
        var handler = Answering.Ok();
        var client = new DeliveryClient(new ReleasedConfig { Url = "https://delivery.test/", Project = "p1", Key = "secret" }, Spec.Options(handler, maxCachedResponses: 0));

        await client.EntriesAsync("v1.0.0", new ListOptions { Type = "article", Order = "-rank", Limit = 3, Locale = "en-US" }, None);
        await client.EntryBySlugAsync("release/1", "a b", cancellationToken: None);
        await client.EntriesByIdAsync("prod", ["id-2", "id-1"], new ReadOptions { Depth = 1 }, None);

        Assert.Equal(
            [
                "https://delivery.test/v1/projects/p1/v1.0.0/entries?limit=3&locale=en-US&order=-rank&type=article",
                "https://delivery.test/v1/projects/p1/release%2F1/entries-by-slug/a%20b",
                "https://delivery.test/v1/projects/p1/prod/entries-by-id?depth=1&ids=id-2,id-1",
            ],
            handler.Calls.Select(call => call.Url));
        Assert.All(handler.Calls, call =>
        {
            Assert.Equal("Bearer secret", call.Header("authorization"));
            Assert.DoesNotContain("secret", call.Url, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_mounted_config_replaces_the_url_and_project_for_mounts_they_cannot_express()
    {
        var handler = Answering.Ok();
        var client = new DeliveryClient(new MountedConfig { BaseUrl = "https://proxy.test/coho/v1/projects/p1/" }, Spec.Options(handler, maxCachedResponses: 0));

        await client.EntriesAsync("prod", cancellationToken: None);

        Assert.Equal("https://proxy.test/coho/v1/projects/p1/prod/entries", handler.Calls[0].Url);
        Assert.Null(handler.Calls[0].Header("authorization"));
    }

    [Fact]
    public void A_client_needs_a_url_and_a_project()
    {
        Assert.Throws<ArgumentException>(() => new DeliveryClient(new ReleasedConfig { Url = "https://delivery.test", Project = " " }));
    }

    [Fact]
    public void A_credential_is_never_printed()
    {
        var released = new ReleasedConfig { Url = "https://delivery.test", Project = "p1", Key = "secret" };
        var preview = new PreviewConfig { Url = "https://coho.test", Account = "acme", Project = "p1", Token = Spec.Token };

        Assert.DoesNotContain("secret", released.ToString(), StringComparison.Ordinal);
        Assert.Contains("Key = set", released.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Spec.Token, preview.ToString(), StringComparison.Ordinal);
    }

    /* PREVIEWS */
    /* PREVIEWS */
    /* PREVIEWS */

    [Fact]
    public async Task A_preview_reads_through_Cohos_preview_mount_with_the_token_in_a_header()
    {
        var handler = Answering.Ok(cacheControl: "no-cache");
        var client = new DeliveryClient(new PreviewConfig { Url = "https://coho.test/", Account = "acme", Project = "p1", Token = Spec.Token }, Spec.Options(handler));

        await client.EntriesAsync("dev", new ListOptions { Type = "article", Locale = "en-US" }, None);
        await client.EntryBySlugAsync("feature/x", "hello", cancellationToken: None);

        Assert.Equal(
            [
                "https://coho.test/api/v1/accounts/acme/projects/p1/preview/dev/entries?locale=en-US&type=article",
                "https://coho.test/api/v1/accounts/acme/projects/p1/preview/feature%2Fx/entries-by-slug/hello",
            ],
            handler.Calls.Select(call => call.Url));
        Assert.All(handler.Calls, call =>
        {
            Assert.Equal($"Bearer {Spec.Token}", call.Header("authorization"));
            Assert.DoesNotContain(Spec.Token, call.Url, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_preview_is_never_answered_from_the_cache()
    {
        var handler = Answering.Ok(cacheControl: "no-cache");
        var client = new DeliveryClient(new PreviewConfig { Url = "https://coho.test", Account = "acme", Project = "p1", Token = Spec.Token }, Spec.Options(handler));

        await client.EntriesAsync("dev", cancellationToken: None);
        await client.EntriesAsync("dev", cancellationToken: None);

        Assert.Equal(2, handler.Calls.Count);
        Assert.Null(handler.Calls[1].Header("if-none-match"));
    }

    [Fact]
    public void A_preview_needs_its_account_and_a_project_token()
    {
        var missing = Assert.Throws<ArgumentException>(() => new DeliveryClient(new PreviewConfig { Url = "https://coho.test", Account = "", Project = "p1", Token = Spec.Token }));
        Assert.Contains("url, account and project", missing.Message, StringComparison.Ordinal);

        var signIn = Assert.Throws<ArgumentException>(() => new DeliveryClient(new PreviewConfig { Url = "https://coho.test", Account = "acme", Project = "p1", Token = "eyJhbGciOi" }));
        Assert.Contains("starting coho_pt_", signIn.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_says_what_to_check_for_what_is_being_read()
    {
        static Answering Problem(string code) => new(_ => Answering.Json(
            JsonSerializer.Serialize(new { code, title = "Not found", status = 404, detail = "No such thing" }),
            HttpStatusCode.NotFound,
            "application/problem+json"));
        DeliveryClient Preview(string code) => new(new PreviewConfig { Url = "https://coho.test", Account = "acme", Project = "p1", Token = Spec.Token }, Spec.Options(Problem(code)));
        DeliveryClient Released(string code) => new(new ReleasedConfig { Url = "https://delivery.test", Project = "p1", Key = Spec.Key }, Spec.Options(Problem(code)));

        var token = await Assert.ThrowsAsync<CohoDeliveryException>(() => Preview("PROJECT_NOT_FOUND").EntriesAsync("dev", cancellationToken: None));
        Assert.Contains("token was issued for this project", token.Message, StringComparison.Ordinal);
        var release = await Assert.ThrowsAsync<CohoDeliveryException>(() => Preview("REF_NOT_FOUND").EntriesAsync("prod", cancellationToken: None));
        Assert.Contains("a release is read from delivery", release.Message, StringComparison.Ordinal);
        var branch = await Assert.ThrowsAsync<CohoDeliveryException>(() => Released("REF_NOT_FOUND").EntriesAsync("dev", cancellationToken: None));
        Assert.Contains("read a branch, or dev, with a project token", branch.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_id_list_asks_nothing()
    {
        var handler = Answering.Ok();
        var client = Mounted(handler);

        var graph = await client.EntriesByIdAsync("v0.0.1", [], cancellationToken: None);

        Assert.Empty(graph.Roots);
        Assert.Empty(graph.Entries);
        Assert.Empty(handler.Calls);
    }

    /* RECORDED ANSWERS */
    /* RECORDED ANSWERS */
    /* RECORDED ANSWERS */

    [Fact]
    public async Task A_recorded_read_comes_back_as_the_server_sent_it()
    {
        var client = Mounted(new Replay(), Spec.Key);

        var bySlug = await client.EntryBySlugAsync("v0.0.1", "chain-1", new ReadOptions { Depth = 2, Locale = "en-US" }, None);
        var ordered = await client.EntriesAsync("v0.0.1", new ListOptions { Type = "article", Order = "-rank", Limit = 3, Locale = "en-US" }, None);

        Assert.True(JsonNode.DeepEquals(Spec.Body("entry-by-slug-depth-2"), Spec.Json(bySlug)));
        Assert.True(JsonNode.DeepEquals(Spec.Body("entries-ordered"), JsonSerializer.SerializeToNode(ordered, DeliveryJson.Default.EntryList)));
    }

    [Fact]
    public async Task A_missing_entry_is_null_not_an_error()
    {
        var client = Mounted(new Replay(), Spec.Key);

        Assert.Null(await client.EntryBySlugAsync("v0.0.1", "nope", cancellationToken: None));
    }

    [Fact]
    public async Task Refusals_throw_with_the_servers_code()
    {
        var keyed = Mounted(new Replay(), Spec.Key);

        var noRef = await Assert.ThrowsAsync<CohoDeliveryException>(() => keyed.EntriesAsync("v9.9.9", cancellationToken: None));
        Assert.Equal(("REF_NOT_FOUND", 404), (noRef.Code, noRef.Status));
        var invalid = await Assert.ThrowsAsync<CohoDeliveryException>(() => keyed.EntriesAsync("v0.0.1", new ListOptions { Order = "rank" }, None));
        Assert.Equal(("INVALID_QUERY", 400), (invalid.Code, invalid.Status));
        var keyless = await Assert.ThrowsAsync<CohoDeliveryException>(() => Mounted(new Replay()).EntriesAsync("v0.0.1", cancellationToken: None));
        Assert.Equal(("KEY_REQUIRED", 401), (keyless.Code, keyless.Status));
        Assert.Equal("KEY_REQUIRED", keyless.Problem?.Code);
    }

    [Fact]
    public async Task A_key_for_another_project_says_so_though_delivery_answers_as_if_the_project_did_not_exist()
    {
        var client = Mounted(new Replay(), Spec.OtherKey);

        var refused = await Assert.ThrowsAsync<CohoDeliveryException>(() => client.EntriesAsync("v0.0.1", cancellationToken: None));

        Assert.Equal("PROJECT_NOT_FOUND", refused.Code);
        Assert.Contains("key was issued for this project", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_that_is_not_a_problem_document_is_UNEXPECTED_RESPONSE()
    {
        var client = Mounted(new Answering(_ => Answering.Json("<h1>Bad gateway</h1>", HttpStatusCode.BadGateway, "text/html")));

        var refused = await Assert.ThrowsAsync<CohoDeliveryException>(() => client.EntriesAsync("v0.0.1", cancellationToken: None));

        Assert.Equal((CohoDeliveryException.UnexpectedResponse, 502), (refused.Code, refused.Status));
    }

    [Fact]
    public async Task A_success_that_is_not_the_contracts_shape_is_UNEXPECTED_RESPONSE()
    {
        var client = Mounted(new Answering(_ => Answering.Json("""{ "roots": "nope" }""")));

        var refused = await Assert.ThrowsAsync<CohoDeliveryException>(() => client.EntriesAsync("v0.0.1", cancellationToken: None));

        Assert.Equal((CohoDeliveryException.UnexpectedResponse, 200), (refused.Code, refused.Status));
    }

    /* CACHING */
    /* CACHING */
    /* CACHING */

    [Fact]
    public async Task A_tag_named_directly_is_fetched_once()
    {
        var replay = new Replay();
        var client = Mounted(replay, Spec.Key);

        var first = await client.EntryBySlugAsync("v0.0.1", "chain-1", new ReadOptions { Locale = "en-US" }, None);
        var second = await client.EntryBySlugAsync("v0.0.1", "chain-1", new ReadOptions { Locale = "en-US" }, None);

        Assert.Single(replay.Calls);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task An_environment_is_revalidated_after_its_max_age_and_a_304_reuses_the_body()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddSeconds(1000));
        var status = HttpStatusCode.OK;
        var handler = new Answering(_ => status == HttpStatusCode.NotModified
            ? NotModified()
            : Answering.Json("""{ "roots": [], "entries": {}, "unresolved": {}, "hasMore": false }""", cacheControl: "public, max-age=30", etag: "\"e1\""));
        var client = Mounted(handler, time: time);

        var first = await client.EntriesAsync("prod", cancellationToken: None);
        time.Advance(TimeSpan.FromSeconds(10));
        await client.EntriesAsync("prod", cancellationToken: None);
        Assert.Single(handler.Calls);

        time.Advance(TimeSpan.FromSeconds(25));
        status = HttpStatusCode.NotModified;
        var again = await client.EntriesAsync("prod", cancellationToken: None);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Equal("\"e1\"", handler.Calls[1].Header("if-none-match"));
        Assert.Same(first, again);

        static HttpResponseMessage NotModified()
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotModified);
            response.Headers.TryAddWithoutValidation("Cache-Control", "public, max-age=30");
            response.Headers.TryAddWithoutValidation("ETag", "\"e1\"");
            return response;
        }
    }

    [Fact]
    public async Task No_store_is_never_kept_and_a_client_without_a_cache_always_asks()
    {
        var noStore = Answering.Ok(cacheControl: "no-store", etag: "\"e1\"");
        var storing = Mounted(noStore);
        await storing.EntriesAsync("prod", cancellationToken: None);
        await storing.EntriesAsync("prod", cancellationToken: None);
        Assert.Equal(2, noStore.Calls.Count);
        Assert.Null(noStore.Calls[1].Header("if-none-match"));

        var immutable = Answering.Ok(cacheControl: "public, max-age=31536000, immutable");
        var uncached = Mounted(immutable, maxCachedResponses: 0);
        await uncached.EntriesAsync("v1.0.0", cancellationToken: None);
        await uncached.EntriesAsync("v1.0.0", cancellationToken: None);
        Assert.Equal(2, immutable.Calls.Count);
    }

    [Fact]
    public void The_cache_drops_the_least_recently_used_response_first()
    {
        var cache = new ResponseCache(2);
        var later = DateTimeOffset.MaxValue;
        cache.Set("a", new Cached("A", null, later));
        cache.Set("b", new Cached("B", null, later));
        Assert.NotNull(cache.Get("a"));

        cache.Set("c", new Cached("C", null, later));

        Assert.NotNull(cache.Get("a"));
        Assert.Null(cache.Get("b"));
        Assert.NotNull(cache.Get("c"));
    }
}
