namespace CohoCms.Delivery;

/// <summary>
/// A private cache of delivery responses, honouring <c>Cache-Control</c> and <c>ETag</c>.
/// </summary>
/// <remarks>
/// Private is what makes it allowed: the client holds the key, so it may keep keyed
/// responses that a shared cache must not. A tag named directly is <c>immutable</c> for a
/// year and is fetched once. An environment is fresh for its short <c>max-age</c>, then
/// revalidated with <c>If-None-Match</c>, which costs a <c>304</c> rather than a body when
/// nothing was promoted.
/// </remarks>
internal sealed class ResponseCache
{
    private readonly int _maxEntries;
    private readonly Dictionary<string, LinkedListNode<(string Url, Cached Cached)>> _entries = new(StringComparer.Ordinal);

    // Most recently used first.
    private readonly LinkedList<(string Url, Cached Cached)> _order = new();
    private readonly object _lock = new();

    public ResponseCache(int maxEntries)
    {
        _maxEntries = maxEntries;
    }

    public Cached? Get(string url)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(url, out var node))
            {
                return null;
            }
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Cached;
        }
    }

    public void Set(string url, Cached cached)
    {
        lock (_lock)
        {
            if (_entries.Remove(url, out var previous))
            {
                _order.Remove(previous);
            }
            _entries[url] = _order.AddFirst((url, cached));
            if (_entries.Count > _maxEntries)
            {
                var oldest = _order.Last!;
                _order.RemoveLast();
                _entries.Remove(oldest.Value.Url);
            }
        }
    }

    public void Delete(string url)
    {
        lock (_lock)
        {
            if (_entries.Remove(url, out var node))
            {
                _order.Remove(node);
            }
        }
    }

    /// <summary>
    /// Until when a response may be used without asking, from its <c>Cache-Control</c>,
    /// or null when it must not be stored at all. A response that makes no claim is stale
    /// at once, so it is only ever reused after a <c>304</c>.
    /// </summary>
    public static DateTimeOffset? FreshUntil(string? cacheControl, DateTimeOffset now)
    {
        var noStore = false;
        var noCache = false;
        long? maxAge = null;

        foreach (var directive in (cacheControl ?? "").Split(','))
        {
            var parts = directive.Trim().ToLowerInvariant().Split('=', 2);
            var name = parts[0];
            if (name == "no-store")
            {
                noStore = true;
            }
            else if (name == "no-cache")
            {
                noCache = true;
            }
            else if (name == "max-age" && parts.Length == 2 && long.TryParse(parts[1], out var seconds) && seconds >= 0)
            {
                maxAge = seconds;
            }
        }

        if (noStore)
        {
            return null;
        }
        if (noCache || maxAge is null)
        {
            return now;
        }
        return now.AddSeconds(Math.Min(maxAge.Value, 10L * 365 * 24 * 60 * 60));
    }
}

// The body is the parsed response, immutable, so it is safe to hand to every caller.
// FreshUntil is until when it may be used without asking.
internal sealed record Cached(object Body, string? ETag, DateTimeOffset FreshUntil);
