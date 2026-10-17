using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CohoCms.Delivery;

// The delivery API's response shapes, as it sends them.
//
// Immutable throughout: a response may be shared with the client's cache, so no caller
// can change what a later caller is served.

/// <summary>One entry: one locale's values, flattened.</summary>
public sealed record Entry
{
    public required string Id { get; init; }

    /// <summary>Unique on its ref.</summary>
    public required string Slug { get; init; }

    /// <summary>The content type's slug.</summary>
    public required string Type { get; init; }

    /// <summary>The locale this entry was projected for. Null when none was requested.</summary>
    public string? Locale { get; init; }

    /// <summary>
    /// The author's fields. A localized field with no value in the requested locale is
    /// absent, not null. A reference holds an entry id, or an array of them.
    /// </summary>
    public ImmutableDictionary<string, JsonElement> Fields
    {
        get => _fields;
        init => _fields = value ?? ImmutableDictionary<string, JsonElement>.Empty;
    }

    // Each default is kept in its setter as well: deserialization sets a key it does not find
    // to null, past an initializer.
    private readonly ImmutableDictionary<string, JsonElement> _fields = ImmutableDictionary<string, JsonElement>.Empty;
}

/// <summary>What every read returns: the roots, everything reached from them, and what was not.</summary>
public record EntryGraph
{
    private readonly ImmutableArray<string> _roots = [];
    private readonly ImmutableDictionary<string, Entry> _entries = ImmutableDictionary<string, Entry>.Empty;
    private readonly ImmutableDictionary<string, string> _unresolved = ImmutableDictionary<string, string>.Empty;

    /// <summary>The entries asked for that exist, in the order the read defines.</summary>
    public ImmutableArray<string> Roots
    {
        get => _roots;
        init => _roots = value.IsDefault ? [] : value;
    }

    /// <summary>The roots and every entry reached from them, keyed by id.</summary>
    public ImmutableDictionary<string, Entry> Entries
    {
        get => _entries;
        init => _entries = value ?? ImmutableDictionary<string, Entry>.Empty;
    }

    /// <summary>
    /// Why a referenced entry is not in <see cref="Entries"/>, by id: see <see cref="UnresolvedReason"/>.
    /// </summary>
    public ImmutableDictionary<string, string> Unresolved
    {
        get => _unresolved;
        init => _unresolved = value ?? ImmutableDictionary<string, string>.Empty;
    }

    internal static readonly EntryGraph Nothing = new();
}

/// <summary>A page of a list.</summary>
public sealed record EntryList : EntryGraph
{
    public bool HasMore { get; init; }
}

/// <summary>
/// Why a requested or referenced entry is not there. Open: a reason the server adds
/// later is passed through as it is.
/// </summary>
public static class UnresolvedReason
{
    /// <summary>Beyond the requested depth.</summary>
    public const string Depth = "depth";

    /// <summary>Not on the ref.</summary>
    public const string Absent = "absent";

    /// <summary>Following the link would loop back to an entry already on the way to it. Set by hydration.</summary>
    public const string Cycle = "cycle";

    /// <summary>
    /// The target is of another type than the field's. Set by typed hydration, which
    /// .NET's types make possible to notice, after a field's targets change.
    /// </summary>
    public const string WrongType = "type";
}

/// <summary>An RFC 9457 problem document. Branch on <see cref="Code"/>, not on the status.</summary>
public sealed record Problem
{
    public required string Code { get; init; }
    public string Title { get; init; } = "";
    public int Status { get; init; }
    public string? Type { get; init; }
    public string? Detail { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EntryGraph))]
[JsonSerializable(typeof(EntryList))]
[JsonSerializable(typeof(Problem))]
internal sealed partial class DeliveryJson : JsonSerializerContext;
