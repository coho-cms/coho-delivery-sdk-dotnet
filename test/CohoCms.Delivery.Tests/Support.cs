using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CohoCms.Delivery.Tests;

/// <summary>One request to the snapshot implementation and its answer, recorded by coho-data.</summary>
internal sealed record Recording(string Name, string Path, string Auth, int Status, string? ContentType, string? CacheControl, bool ETag, JsonNode? Body);

/// <summary>A request a test's handler was sent.</summary>
internal sealed record Call(string Url, IReadOnlyDictionary<string, string> Headers)
{
    public string? Header(string name) => Headers.GetValueOrDefault(name.ToLowerInvariant());
}

internal static class Spec
{
    public const string Base = "https://delivery.test/v1/projects/p1";
    public const string Key = "test-key";
    public const string OtherKey = "other-key";
    public static readonly string Token = $"coho_pt_{new string('a', 43)}";

    private static readonly string Directory = System.IO.Path.Combine(AppContext.BaseDirectory, "spec", "delivery", "recordings");

    public static Recording Recording(string name)
    {
        var json = JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(Directory, $"{name}.json")))!;
        var request = json["request"]!;
        var response = json["response"]!;
        var headers = response["headers"]!;
        return new Recording(
            name,
            (string)request["path"]!,
            (string)request["auth"]!,
            (int)response["status"]!,
            (string?)headers["content-type"],
            (string?)headers["cache-control"],
            (bool)headers["etag"]!,
            response["body"]);
    }

    /// <summary>A recording's body, as JSON, to compare a client's answer with.</summary>
    public static JsonNode Body(string name) => Recording(name).Body!;

    /// <summary>A recording's body, as the graph it is.</summary>
    public static EntryGraph Graph(string name) => Body(name).Deserialize(DeliveryJson.Default.EntryList)!;

    /// <summary>What a client answered, as JSON, to compare with a recording.</summary>
    public static JsonNode? Json(EntryGraph? graph) => graph is null ? null : JsonSerializer.SerializeToNode(graph, DeliveryJson.Default.EntryGraph);

    public static ClientOptions Options(HttpMessageHandler handler, TimeProvider? time = null, int maxCachedResponses = 1000)
    {
        return new ClientOptions
        {
            HttpClient = new HttpClient(handler),
            TimeProvider = time ?? TimeProvider.System,
            MaxCachedResponses = maxCachedResponses,
        };
    }

    public static IEnumerable<Recording> All()
    {
        return System.IO.Directory.GetFiles(Directory, "*.json")
            .Select(file => Recording(System.IO.Path.GetFileNameWithoutExtension(file)));
    }
}

/// <summary>A handler that records what it is sent, and answers with <paramref name="answer"/>.</summary>
internal sealed class Answering(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public List<Call> Calls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(header => header.Key.ToLowerInvariant(), header => string.Join(", ", header.Value));
        Calls.Add(new Call(request.RequestUri!.AbsoluteUri, headers));
        return Task.FromResult(answer(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, string type = "application/json", string? cacheControl = null, string? etag = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, type) };
        if (cacheControl is not null)
        {
            response.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        }
        if (etag is not null)
        {
            response.Headers.TryAddWithoutValidation("ETag", etag);
        }
        return response;
    }

    public static Answering Ok(string? cacheControl = null, string? etag = null)
    {
        return new Answering(_ => Json("""{ "roots": [], "entries": {}, "unresolved": {}, "hasMore": false }""", cacheControl: cacheControl, etag: etag));
    }
}

/// <summary>
/// A handler that answers from the recordings, as the server did, plus <c>304</c> for an
/// <c>If-None-Match</c> naming the recording's ETag. Each recording's ETag is its name.
/// </summary>
internal sealed class Replay : HttpMessageHandler
{
    private readonly List<Recording> _all = Spec.All().ToList();

    public List<Call> Calls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        var headers = request.Headers.ToDictionary(header => header.Key.ToLowerInvariant(), header => string.Join(", ", header.Value));
        Calls.Add(new Call(url, headers));

        if (!url.StartsWith(Spec.Base, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Not a delivery URL: {url}");
        }
        var authorization = headers.GetValueOrDefault("authorization");
        var auth = authorization is null ? "none" : authorization == $"Bearer {Spec.Key}" ? "key" : "other-key";
        var wanted = Normalize(url[Spec.Base.Length..]);
        var match = _all.FirstOrDefault(recorded => recorded.Auth == auth && Normalize(recorded.Path) == wanted)
            ?? throw new InvalidOperationException($"Nothing recorded for {auth} {wanted}");

        var etag = match.ETag ? $"\"{match.Name}\"" : null;
        HttpResponseMessage response;
        if (etag is not null && headers.GetValueOrDefault("if-none-match") == etag)
        {
            response = new HttpResponseMessage(HttpStatusCode.NotModified) { Content = new ByteArrayContent([]) };
        }
        else
        {
            response = new HttpResponseMessage((HttpStatusCode)match.Status) { Content = new StringContent(match.Body?.ToJsonString() ?? "") };
            response.Content.Headers.Remove("Content-Type");
            if (match.ContentType is not null)
            {
                response.Content.Headers.TryAddWithoutValidation("Content-Type", match.ContentType);
            }
        }
        if (match.CacheControl is not null)
        {
            response.Headers.TryAddWithoutValidation("Cache-Control", match.CacheControl);
        }
        if (etag is not null)
        {
            response.Headers.TryAddWithoutValidation("ETag", etag);
        }
        return Task.FromResult(response);
    }

    /// <summary>The path, and the query in name order and decoded, so equal requests compare equal.</summary>
    private static string Normalize(string pathAndQuery)
    {
        var parts = pathAndQuery.Split('?', 2);
        IEnumerable<string> query = parts.Length == 2 && parts[1] != ""
            ? parts[1].Split('&').Select(Uri.UnescapeDataString).Order(StringComparer.Ordinal)
            : [];
        return $"{Uri.UnescapeDataString(parts[0])}?{string.Join('&', query)}";
    }
}
