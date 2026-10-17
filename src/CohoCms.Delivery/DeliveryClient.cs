using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CohoCms.Delivery;

public record ReadOptions
{
    /// <summary>How many reference hops to follow from each root, 0 to 5. The server's default is 0.</summary>
    public int? Depth { get; init; }

    /// <summary>The locale to project to. Without one, localized fields are absent.</summary>
    public string? Locale { get; init; }
}

public sealed record ListOptions : ReadOptions
{
    public string? Type { get; init; }

    /// <summary>A sortable field of <see cref="Type"/>, prefixed <c>-</c> for descending. Needs <see cref="Type"/>.</summary>
    public string? Order { get; init; }

    public int? Limit { get; init; }

    public int? Offset { get; init; }
}

/// <summary>The delivery API, untyped: entries come back as the server sends them.</summary>
/// <remarks>Safe to share: one client, and its cache, for the life of the app.</remarks>
public sealed class DeliveryClient
{
    private static readonly HttpClient Shared = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });

    private readonly DeliveryConfig _config;
    private readonly string _base;
    private readonly Func<HttpClient> _http;
    private readonly TimeProvider _time;
    private readonly ResponseCache? _cache;

    public DeliveryClient(DeliveryConfig config, ClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        options ??= new ClientOptions();
        Check(config);

        _config = config;
        _base = config.Base;
        _time = options.TimeProvider;
        _cache = options.MaxCachedResponses > 0 ? new ResponseCache(options.MaxCachedResponses) : null;
        if (options.HttpClient is { } http)
        {
            _http = () => http;
        }
        else if (options.HttpClientFactory is { } factory)
        {
            var name = options.HttpClientName;
            _http = () => factory.CreateClient(name);
        }
        else
        {
            _http = () => Shared;
        }
    }

    /// <summary>A page of entries on <paramref name="ref"/>, in the order <see cref="ListOptions.Order"/> asks for, or by id.</summary>
    public Task<EntryList> EntriesAsync(string @ref, ListOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ListOptions();
        return GetAsync(
            $"{At(@ref)}/entries",
            [
                ("type", options.Type),
                ("order", options.Order),
                ("limit", Text(options.Limit)),
                ("offset", Text(options.Offset)),
                ("depth", Text(options.Depth)),
                ("locale", options.Locale),
            ],
            DeliveryJson.Default.EntryList,
            cancellationToken);
    }

    /// <summary>One entry by id, or null if it is not on <paramref name="ref"/>.</summary>
    public Task<EntryGraph?> EntryAsync(string @ref, string id, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        return OrNullAsync(GetAsync($"{At(@ref)}/entries/{Uri.EscapeDataString(id)}", Read(options), DeliveryJson.Default.EntryGraph, cancellationToken));
    }

    /// <summary>One entry by slug, or null if it is not on <paramref name="ref"/>.</summary>
    public Task<EntryGraph?> EntryBySlugAsync(string @ref, string slug, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        return OrNullAsync(GetAsync($"{At(@ref)}/entries-by-slug/{Uri.EscapeDataString(slug)}", Read(options), DeliveryJson.Default.EntryGraph, cancellationToken));
    }

    /// <summary>
    /// Several entries by id, at most 100. An id not on <paramref name="ref"/> is reported
    /// under <see cref="EntryGraph.Unresolved"/> as <see cref="UnresolvedReason.Absent"/>.
    /// </summary>
    public Task<EntryGraph> EntriesByIdAsync(string @ref, IReadOnlyCollection<string> ids, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Task.FromResult(EntryGraph.Nothing);
        }
        return GetAsync($"{At(@ref)}/entries-by-id", [("ids", string.Join(',', ids)), .. Read(options)], DeliveryJson.Default.EntryGraph, cancellationToken);
    }

    /* REQUESTS */
    /* REQUESTS */
    /* REQUESTS */

    private async Task<T> GetAsync<T>(string path, (string Name, string? Value)[] query, JsonTypeInfo<T> type, CancellationToken cancellationToken)
        where T : class
    {
        var url = $"{_base}{path}{QueryString(query)}";
        var cached = _cache?.Get(url);
        if (cached is not null && cached.FreshUntil > _time.GetUtcNow())
        {
            return (T)cached.Body;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_config.Credential is { } credential)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }
        if (cached?.ETag is { } etag)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using var response = await _http().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            Remember(url, cached.Body, response, cached.ETag);
            return (T)cached.Body;
        }
        if (!response.IsSuccessStatusCode)
        {
            throw await RefusalAsync(response, _config.Reads, cancellationToken).ConfigureAwait(false);
        }

        T? body;
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                body = await JsonSerializer.DeserializeAsync(stream, type, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException exception)
        {
            throw new CohoDeliveryException(status, CohoDeliveryException.UnexpectedResponse, $"Delivery answered {status} with a body that is not what the contract describes: {exception.Message}");
        }
        if (body is null)
        {
            throw new CohoDeliveryException(status, CohoDeliveryException.UnexpectedResponse, $"Delivery answered {status} with null");
        }
        Remember(url, body, response);
        return body;
    }

    private void Remember(string url, object body, HttpResponseMessage response, string? previousETag = null)
    {
        if (_cache is null)
        {
            return;
        }
        var now = _time.GetUtcNow();
        var until = ResponseCache.FreshUntil(response.Headers.CacheControl?.ToString() ?? Header(response, "Cache-Control"), now);
        var etag = response.Headers.ETag?.ToString() ?? previousETag;

        // Nothing to gain from keeping a body that can neither be served nor revalidated.
        if (until is null || (until <= now && etag is null))
        {
            _cache.Delete(url);
        }
        else
        {
            _cache.Set(url, new Cached(body, etag, until.Value));
        }
    }

    /// <summary>A single read's refusal for a missing entry is a null, not an error.</summary>
    private static async Task<EntryGraph?> OrNullAsync(Task<EntryGraph> read)
    {
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch (CohoDeliveryException exception) when (exception.Code == "ENTRY_NOT_FOUND")
        {
            return null;
        }
    }

    private static string At(string @ref) => $"/{Uri.EscapeDataString(@ref)}";

    private static (string, string?)[] Read(ReadOptions? options) => [("depth", Text(options?.Depth)), ("locale", options?.Locale)];

    private static string? Text(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;
    }

    /// <summary>
    /// Parameters in name order, whatever order the caller gave, so two call sites asking
    /// the same question share one URL, and so one cache entry here and one at any edge.
    /// Commas are left readable: <c>ids</c> is a comma-separated list.
    /// </summary>
    internal static string QueryString((string Name, string? Value)[] query)
    {
        var parts = query
            .Where(part => part.Value is not null)
            .OrderBy(part => part.Name, StringComparer.Ordinal)
            .Select(part => $"{part.Name}={Uri.EscapeDataString(part.Value!).Replace("%2C", ",", StringComparison.OrdinalIgnoreCase)}")
            .ToList();
        return parts.Count == 0 ? "" : $"?{string.Join('&', parts)}";
    }

    /* CHECKS */
    /* CHECKS */
    /* CHECKS */

    /// <summary>
    /// What the types cannot refuse: an empty value, a token that is not a project token,
    /// and a token in a browser.
    /// </summary>
    private static void Check(DeliveryConfig config)
    {
        switch (config)
        {
            case ReleasedConfig released:
                Present(released.Url, "Coho delivery needs a url", nameof(config));
                Present(released.Project, "Coho delivery needs a project", nameof(config));
                break;
            case MountedConfig mounted:
                Present(mounted.BaseUrl, "Coho delivery needs a base url", nameof(config));
                break;
            case PreviewConfig preview:
                const string Needs = "A Coho preview needs the url, account and project with its token";
                Present(preview.Url, Needs, nameof(config));
                Present(preview.Account, Needs, nameof(config));
                Present(preview.Project, Needs, nameof(config));
                if (preview.Token is null || !preview.Token.StartsWith(PreviewConfig.TokenPrefix, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"A Coho preview token is a project token, starting {PreviewConfig.TokenPrefix}", nameof(config));
                }

                // A token in a browser is a token anyone can read, and it reads everything unreleased.
                if (OperatingSystem.IsBrowser())
                {
                    throw new ArgumentException("A Coho project token must stay on the server: this code is running in a browser", nameof(config));
                }
                break;
            default:
                throw new ArgumentException($"Not a Coho delivery config: {config.GetType().Name}", nameof(config));
        }
    }

    private static void Present(string? value, string message, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }
    }

    /* REFUSALS */
    /* REFUSALS */
    /* REFUSALS */

    private static async Task<CohoDeliveryException> RefusalAsync(HttpResponseMessage response, Reads reads, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var problem = await ProblemOfAsync(response, cancellationToken).ConfigureAwait(false);
        if (problem is null)
        {
            return new CohoDeliveryException(status, CohoDeliveryException.UnexpectedResponse, $"Delivery answered {status} with no problem document");
        }
        var text = (problem.Detail ?? problem.Title).TrimEnd('.');
        var hint = Hints[reads].GetValueOrDefault(problem.Code, "");
        return new CohoDeliveryException(status, problem.Code, $"{problem.Code}: {text}.{hint}", problem);
    }

    /// <summary>
    /// What to check, for the refusals whose answer is deliberately vague. A credential for
    /// another project is answered exactly as a project that does not exist, so that one
    /// customer's cannot confirm another's project; and each side serves only its own refs.
    /// </summary>
    private static readonly Dictionary<Reads, Dictionary<string, string>> Hints = new()
    {
        [Reads.Released] = new()
        {
            ["PROJECT_NOT_FOUND"] = " Check that the key was issued for this project.",
            ["REF_NOT_FOUND"] = " Delivery serves tags and the environments pointing at them; read a branch, or dev, with a project token.",
        },
        [Reads.Preview] = new()
        {
            ["PROJECT_NOT_FOUND"] = " Check that the token was issued for this project, and that the account is the one it belongs to.",
            ["REF_NOT_FOUND"] = " A preview reads branches, and environments pointing at one such as dev; a release is read from delivery, with a key.",
        },
    };

    private static async Task<Problem?> ProblemOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Read either JSON type. The contract says `application/problem+json`, and servers
        // before 2026-10-13 labelled problems `application/json`.
        var type = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!type.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            return new Problem
            {
                Code = code.GetString()!,
                Title = StringOf(root, "title") ?? "",
                Status = root.TryGetProperty("status", out var status) && status.TryGetInt32(out var number) ? number : (int)response.StatusCode,
                Type = StringOf(root, "type"),
                Detail = StringOf(root, "detail"),
            };
        }
        catch (JsonException)
        {
            // Labelled JSON and not JSON: no problem document to report.
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string? StringOf(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
