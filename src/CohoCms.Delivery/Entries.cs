using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CohoCms.Delivery;

/// <summary>
/// What every hydrated entry has. Generated code derives a record for each content type
/// from it; an entry of a type nobody declared is a <see cref="HydratedEntry"/>.
/// </summary>
/// <remarks>
/// A hydrated entry is plain data: its references are replaced by the entries they
/// point at, or by stubs, and it holds no cycles, so it can be serialized as it is.
/// </remarks>
public abstract record EntryBase
{
    protected EntryBase()
    {
    }

    /// <summary>The system values of the entry <paramref name="entry"/> is reading.</summary>
    [SetsRequiredMembers]
    protected EntryBase(EntryReader entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Id = entry.Id;
        Type = entry.Type;
        Slug = entry.Slug;
        Locale = entry.Locale;
    }

    public required string Id { get; init; }

    /// <summary>The content type's slug.</summary>
    public required string Type { get; init; }

    public required string Slug { get; init; }

    /// <summary>The locale the entry was projected for. Null when none was requested.</summary>
    public string? Locale { get; init; }
}

/// <summary>An entry, untyped: its fields as delivery sent them, and its references followed.</summary>
public sealed record HydratedEntry : EntryBase
{
    private readonly ImmutableDictionary<string, object> _links = ImmutableDictionary<string, object>.Empty;

    public HydratedEntry()
    {
    }

    [SetsRequiredMembers]
    internal HydratedEntry(EntryReader entry, TypeShape? shape)
        : base(entry)
    {
        Fields = entry.Fields;
        var links = ImmutableDictionary.CreateBuilder<string, object>(StringComparer.Ordinal);
        foreach (var (field, kind) in shape?.Links ?? ImmutableDictionary<string, LinkKind>.Empty)
        {
            object? linked = kind == LinkKind.Many ? entry.Links<EntryBase>(field) : entry.Link<EntryBase>(field);
            if (linked is not null)
            {
                links[field] = linked;
            }
        }
        _links = links.ToImmutable();
    }

    /// <summary>Every field as delivery sent it, references as ids.</summary>
    public ImmutableDictionary<string, JsonElement> Fields { get; init; } = ImmutableDictionary<string, JsonElement>.Empty;

    /// <summary>A field's value as delivery sent it, or null when the entry has none.</summary>
    public JsonElement? Field(string field) => Fields.TryGetValue(field, out var value) ? value : null;

    /// <summary>A reference field's target, when the shape names the field as one link.</summary>
    public Linked<EntryBase>? Link(string field) => _links.GetValueOrDefault(field) as Linked<EntryBase>;

    /// <summary>A reference field's targets, when the shape names the field as many links.</summary>
    public IReadOnlyList<Linked<EntryBase>>? Links(string field) => _links.GetValueOrDefault(field) as IReadOnlyList<Linked<EntryBase>>;
}

/// <summary>
/// A reference: its target, or a stub saying why the target is not here.
/// </summary>
public sealed record Linked<T>
    where T : EntryBase
{
    internal Linked(string id, T? value, string? unresolved)
    {
        Id = id;
        Value = value;
        Unresolved = unresolved;
    }

    /// <summary>The target's id, whether or not it is here.</summary>
    public string Id { get; }

    /// <summary>The target, or null when it is not here.</summary>
    public T? Value { get; }

    /// <summary>
    /// Why the target is not here: <see cref="UnresolvedReason.Depth"/>,
    /// <see cref="UnresolvedReason.Absent"/>, <see cref="UnresolvedReason.Cycle"/> or
    /// <see cref="UnresolvedReason.WrongType"/>. Null when it is.
    /// </summary>
    public string? Unresolved { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Unresolved))]
    public bool IsResolved => Value is not null;

    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = Value;
        return value is not null;
    }
}

/// <summary>Makes links, for tests and for code that builds entries itself.</summary>
public static class Linked
{
    /// <summary>A link to an entry that is here.</summary>
    public static Linked<T> To<T>(T value)
        where T : EntryBase
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Linked<T>(value.Id, value, null);
    }

    /// <summary>A link to an entry that is not here, and why: see <see cref="UnresolvedReason"/>.</summary>
    public static Linked<T> Stub<T>(string id, string reason)
        where T : EntryBase
    {
        return new Linked<T>(id, null, reason);
    }

    /// <summary>
    /// The entries that are here, in order: <c>post.Tags.Resolved()</c>. Stubs are left out,
    /// such as a link to a deleted tag, or one beyond the depth the read asked for.
    /// </summary>
    public static IEnumerable<T> Resolved<T>(this IEnumerable<Linked<T>> links)
        where T : EntryBase
    {
        ArgumentNullException.ThrowIfNull(links);
        foreach (var link in links)
        {
            if (link.TryGetValue(out var value))
            {
                yield return value;
            }
        }
    }
}

/// <summary>
/// What every typed repeater item has: its identity, stable across edits, moves and merges,
/// and the component type it is. Generated code derives a record for each component type
/// from it. A repeater that holds several is a list of these: match on the record's type to
/// narrow an item.
/// </summary>
/// <remarks>Plain data, like an entry: an item lives inside the entry that holds it.</remarks>
public abstract record ItemBase
{
    protected ItemBase()
    {
    }

    /// <summary>The system values of the item <paramref name="item"/> is reading.</summary>
    [SetsRequiredMembers]
    protected ItemBase(ItemReader item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Id = item.Id;
        Type = item.Type;
    }

    public required string Id { get; init; }

    /// <summary>The component type's slug.</summary>
    public required string Type { get; init; }
}

/// <summary>An item of a repeater whose component types are not on the ref, so its fields are not typed.</summary>
public sealed record RepeaterItem
{
    public required string Id { get; init; }

    /// <summary>The component type's slug, when the item has one.</summary>
    public string? Type { get; init; }

    /// <summary>Every field but the system keys, as delivery sent it.</summary>
    public ImmutableDictionary<string, JsonElement> Fields { get; init; } = ImmutableDictionary<string, JsonElement>.Empty;
}

/// <summary>Whether a reference field holds one link or an array of them.</summary>
public enum LinkKind
{
    One,
    Many,
}

/// <summary>
/// What hydration needs to know about one content type. Generated code writes it from
/// the type model. Untyped callers may write one by hand, or pass none, in which case
/// references stay ids.
/// </summary>
public sealed record TypeShape
{
    /// <summary>The reference fields, by field id. What a <see cref="HydratedEntry"/> follows.</summary>
    public ImmutableDictionary<string, LinkKind> Links { get; init; } = ImmutableDictionary<string, LinkKind>.Empty;

    /// <summary>The fields delivery always includes: required, and not localized.</summary>
    public ImmutableArray<string> Always { get; init; } = [];

    /// <summary>
    /// Builds the type's record from an entry. Generated code sets it; without it, entries
    /// of the type are <see cref="HydratedEntry"/>.
    /// </summary>
    public Func<EntryReader, EntryBase>? Read { get; init; }
}

/// <summary>What an issue is about.</summary>
public enum IssueKind
{
    /// <summary>A field the type always has is absent, as old entries are after a field becomes required.</summary>
    MissingField,

    /// <summary>A value of another kind than the type says: one link where it says many, or text where it says a number.</summary>
    UnexpectedValue,

    /// <summary>
    /// A link delivery neither resolved nor reported, which means the shape and the
    /// content disagree about the field.
    /// </summary>
    UnlistedLink,

    /// <summary>
    /// A repeater item of a component the code has no type for, left out of the list. The
    /// code was generated before that component was added to the field.
    /// </summary>
    UnknownItem,
}

/// <summary>Something about an entry that its type says cannot happen. Reported, never thrown.</summary>
public sealed record Issue(IssueKind Kind, string Id, string Slug, string Type, string Field, string Message);
