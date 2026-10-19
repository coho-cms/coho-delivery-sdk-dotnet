using Microsoft.Extensions.Logging;

namespace CohoCms.Delivery;

/// <summary>What a content client reads, on top of where it reads it from.</summary>
public sealed record ContentConfig
{
    public required DeliveryConfig Delivery { get; init; }

    /// <summary>
    /// The ref to read: an environment such as <c>prod</c>, or a tag. With a project token,
    /// a branch, or an environment pointing at one such as <c>dev</c>.
    /// </summary>
    public required string Ref { get; init; }

    /// <summary>The locale to project to, unless a read names its own.</summary>
    public string? Locale { get; init; }
}

/// <summary>A page of hydrated entries.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, bool HasMore);

/// <summary>A sort order for a list: a sortable field, ascending or descending.</summary>
/// <remarks>
/// Generated code declares one per content type, holding only the fields that type can be
/// sorted by, so an unsortable order does not compile. A default value means no order.
/// </remarks>
public interface ISortOrder
{
    /// <summary>The field, prefixed <c>-</c> for descending. Null for no order.</summary>
    string? Value { get; }
}

/// <summary>Any field as a sort order, for untyped reads.</summary>
public readonly record struct SortOrder(string? Value) : ISortOrder
{
    public SortOrder Descending => new(Value is null ? null : $"-{Value.TrimStart('-')}");
}

/// <summary>The order of a type with no sortable fields: there is none to give.</summary>
public readonly record struct NoSortOrder : ISortOrder
{
    public string? Value => null;
}

public sealed record TypeListOptions<TOrder> : ReadOptions
    where TOrder : struct, ISortOrder
{
    public TOrder? Order { get; init; }

    public int? Limit { get; init; }

    public int? Offset { get; init; }
}

/// <summary>Reads of one content type, hydrated as <typeparamref name="T"/>.</summary>
public sealed class TypeReader<T, TOrder>
    where T : EntryBase
    where TOrder : struct, ISortOrder
{
    private readonly ContentClient _content;
    private readonly string _slug;
    private readonly IReadOnlyCollection<string> _family;

    internal TypeReader(ContentClient content, string slug, IReadOnlyCollection<string>? family)
    {
        _content = content;
        _slug = slug;
        _family = family ?? [slug];
    }

    /// <summary>The entry with this slug, or null if there is none or it is of another type.</summary>
    public async Task<T?> BySlugAsync(string slug, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        return OfType(await _content.BySlugAsync(slug, options, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The entry with this id, or null if there is none or it is of another type.</summary>
    public async Task<T?> ByIdAsync(string id, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        return OfType(await _content.ByIdAsync(id, options, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Page<T>> ListAsync(TypeListOptions<TOrder>? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new TypeListOptions<TOrder>();
        var page = await _content.Delivery.EntriesAsync(
            _content.Ref,
            new ListOptions
            {
                Type = _slug,
                Order = options.Order?.Value,
                Limit = options.Limit,
                Offset = options.Offset,
                Depth = options.Depth,
                Locale = options.Locale ?? _content.Locale,
            },
            cancellationToken).ConfigureAwait(false);
        return new Page<T>(_content.Hydrate(page).OfType<T>().ToList(), page.HasMore);
    }

    // A slug is unique per ref, not per type, so a typed read checks what it found.
    private T? OfType(EntryBase? entry) => entry is T typed && _family.Contains(entry.Type) ? typed : null;
}

/// <summary>
/// Hydrated reads on one ref. What generated code builds its typed client from, so that
/// the logic lives in this package and reaches every library with an update.
/// </summary>
/// <remarks>Safe to share: one client, and its cache, for the life of the app.</remarks>
public sealed class ContentClient
{
    private readonly IReadOnlyDictionary<string, TypeShape>? _shape;
    private readonly Hydrator.Issues _issues;

    public ContentClient(ContentConfig config, IReadOnlyDictionary<string, TypeShape>? shape = null, ClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        options ??= new ClientOptions();
        Delivery = new DeliveryClient(config.Delivery, options);
        Ref = config.Ref;
        Locale = config.Locale;
        _shape = shape;

        // Once per issue for the life of the client rather than per read, so a page
        // rendered a thousand times reports its broken entry once.
        _issues = new Hydrator.Issues(options.OnIssue ?? LogTo(options.Logger));
    }

    /// <summary>The untyped client underneath, for anything the hydrated reads do not cover.</summary>
    public DeliveryClient Delivery { get; }

    public string Ref { get; }

    public string? Locale { get; }

    /// <summary>The entry with this slug, whatever its type, or null.</summary>
    public async Task<EntryBase?> BySlugAsync(string slug, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        return First(await Delivery.EntryBySlugAsync(Ref, slug, Options(options), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The entry with this id, whatever its type, or null.</summary>
    public async Task<EntryBase?> ByIdAsync(string id, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        return First(await Delivery.EntryAsync(Ref, id, Options(options), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The entries that exist, sorted by id. Ids not on the ref are left out.</summary>
    public async Task<IReadOnlyList<EntryBase>> ByIdsAsync(IReadOnlyCollection<string> ids, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Hydrate(await Delivery.EntriesByIdAsync(Ref, ids, Options(options), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Reads of one content type, ordered by any field.</summary>
    /// <param name="slug">The type's slug, which a list asks delivery for.</param>
    /// <param name="family">For a type others extend, the types an entry read here can be, as delivery lists them; by default, <paramref name="slug"/>'s own.</param>
    public TypeReader<T, SortOrder> Type<T>(string slug, IReadOnlyCollection<string>? family = null)
        where T : EntryBase
    {
        return new TypeReader<T, SortOrder>(this, slug, family);
    }

    /// <summary>Reads of one content type. Generated code names the type and its sortable fields.</summary>
    /// <param name="slug">The type's slug, which a list asks delivery for.</param>
    /// <param name="family">For a type others extend, the types an entry read here can be, as delivery lists them; by default, <paramref name="slug"/>'s own.</param>
    public TypeReader<T, TOrder> Type<T, TOrder>(string slug, IReadOnlyCollection<string>? family = null)
        where T : EntryBase
        where TOrder : struct, ISortOrder
    {
        return new TypeReader<T, TOrder>(this, slug, family);
    }

    internal List<EntryBase> Hydrate(EntryGraph graph) => Hydrator.Hydrate(graph, _shape, _issues);

    private EntryBase? First(EntryGraph? graph)
    {
        var entries = graph is null ? [] : Hydrate(graph);
        return entries.Count > 0 ? entries[0] : null;
    }

    private ReadOptions Options(ReadOptions? asked) => new() { Depth = asked?.Depth, Locale = asked?.Locale ?? Locale };

    private static Action<Issue> LogTo(ILogger? logger)
    {
        if (logger is null)
        {
            return Hydrator.Warn;
        }
        return issue => Log.Issue(logger, issue.Message);
    }
}

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "[coho] {Message}")]
    public static partial void Issue(ILogger logger, string message);
}
