using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace CohoCms.Delivery;

/// <summary>Turns an <see cref="EntryGraph"/> into entries whose references are their targets.</summary>
public static class Hydrator
{
    /// <summary>
    /// The graph's roots, in order, with each reference replaced by its target or a stub.
    /// </summary>
    /// <remarks>
    /// Each root gets its own pass, so its tree holds everything reachable from it. Within
    /// a pass an entry is built once and shared, and a link back to an entry still being
    /// built becomes a <see cref="UnresolvedReason.Cycle"/> stub, which is what keeps the
    /// result acyclic. Problems are reported to <paramref name="onIssue"/>, once each, and
    /// never thrown.
    /// </remarks>
    public static IReadOnlyList<EntryBase> Hydrate(EntryGraph graph, IReadOnlyDictionary<string, TypeShape>? shape = null, Action<Issue>? onIssue = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Hydrate(graph, shape, new Issues(onIssue ?? Warn));
    }

    internal static List<EntryBase> Hydrate(EntryGraph graph, IReadOnlyDictionary<string, TypeShape>? shape, Issues issues)
    {
        var result = new List<EntryBase>(graph.Roots.Length);
        foreach (var rootId in graph.Roots)
        {
            if (graph.Entries.TryGetValue(rootId, out var root))
            {
                result.Add(new Pass(graph, shape, issues).Build(root));
            }
        }
        return result;
    }

    internal static void Warn(Issue issue) => Console.Error.WriteLine($"[coho] {issue.Message}");

    /// <summary>Reports each issue once: an entry met in several trees is one entry.</summary>
    internal sealed class Issues(Action<Issue> onIssue)
    {
        private readonly HashSet<(IssueKind, string, string)> _reported = [];

        public void Report(IssueKind kind, Entry entry, string field, string what)
        {
            if (_reported.Add((kind, entry.Id, field)))
            {
                onIssue(new Issue(kind, entry.Id, entry.Slug, entry.Type, field, $"{entry.Type} '{entry.Slug}' {what}"));
            }
        }
    }

    /// <summary>One root's tree.</summary>
    internal sealed class Pass(EntryGraph graph, IReadOnlyDictionary<string, TypeShape>? shape, Issues issues)
    {
        private readonly Dictionary<string, EntryBase> _built = new(StringComparer.Ordinal);
        private readonly HashSet<string> _building = new(StringComparer.Ordinal);

        public Issues Issues => issues;

        public EntryBase Build(Entry entry)
        {
            _building.Add(entry.Id);
            var type = shape?.GetValueOrDefault(entry.Type);
            var reader = new EntryReader(entry, this);
            var built = type?.Read is { } read ? read(reader) : new HydratedEntry(reader, type);

            foreach (var field in type?.Always ?? [])
            {
                if (!entry.Fields.ContainsKey(field))
                {
                    issues.Report(IssueKind.MissingField, entry, field, $"has no '{field}', which its type requires");
                }
            }

            _building.Remove(entry.Id);
            _built[entry.Id] = built;
            return built;
        }

        public Linked<T> Follow<T>(Entry from, string field, string id)
            where T : EntryBase
        {
            EntryBase? target;
            if (_built.TryGetValue(id, out var done))
            {
                target = done;
            }
            else if (_building.Contains(id))
            {
                return Linked.Stub<T>(id, UnresolvedReason.Cycle);
            }
            else if (graph.Entries.TryGetValue(id, out var entry))
            {
                target = Build(entry);
            }
            else
            {
                if (!graph.Unresolved.TryGetValue(id, out var reason))
                {
                    issues.Report(IssueKind.UnlistedLink, from, field, $"links '{field}' to {id}, which delivery neither returned nor reported");
                }
                return Linked.Stub<T>(id, reason ?? UnresolvedReason.Absent);
            }

            if (target is T typed)
            {
                return Linked.To(typed);
            }
            issues.Report(IssueKind.UnexpectedValue, from, field, $"links '{field}' to {target.Type} '{target.Slug}', which is not a type the field links to");
            return Linked.Stub<T>(id, UnresolvedReason.WrongType);
        }
    }
}

/// <summary>
/// Values read from one map of fields, for the record a generated constructor builds: an
/// entry's (<see cref="EntryReader"/>) or a repeater item's (<see cref="ItemReader"/>). A
/// value of the wrong kind is reported and read as null, so content the code did not expect
/// never stops a page rendering.
/// </summary>
public abstract class FieldReader
{
    private readonly Entry _entry;
    private readonly Hydrator.Pass _pass;
    private readonly string _path;

    /// <param name="entry">The entry the fields are in, which an issue is about.</param>
    /// <param name="pass">The tree being built, which follows links.</param>
    /// <param name="fields">The fields to read.</param>
    /// <param name="path">Where they are in the entry, such as <c>citations/0/</c> for an item: what an issue names.</param>
    private protected FieldReader(Entry entry, Hydrator.Pass pass, ImmutableDictionary<string, JsonElement> fields, string path)
    {
        _entry = entry;
        _pass = pass;
        _path = path;
        Fields = fields;
    }

    /// <summary>Every field as delivery sent it, references as ids.</summary>
    public ImmutableDictionary<string, JsonElement> Fields { get; }

    /// <summary>Text, long text, or a choice.</summary>
    public string? Text(string field) => Value(field, "text", JsonValueKind.String, value => value.GetString());

    /// <summary>A multichoice's values.</summary>
    public IReadOnlyList<string>? Texts(string field) => Array(field, "text", JsonValueKind.String, value => value.GetString()!);

    public long? Integer(string field) => Value(field, "an integer", JsonValueKind.Number, value => value.TryGetInt64(out var number) ? number : (long?)null);

    public double? Number(string field) => Value(field, "a number", JsonValueKind.Number, value => value.GetDouble());

    public bool? Boolean(string field)
    {
        if (!Fields.TryGetValue(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }
        return Unexpected<bool?>(field, value, "true or false");
    }

    /// <summary>An ISO 8601 date and time.</summary>
    public DateTimeOffset? DateTime(string field)
    {
        return Value(
            field,
            "an ISO 8601 date and time",
            JsonValueKind.String,
            value => DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : (DateTimeOffset?)null);
    }

    /// <summary>Any value, as delivery sent it: a json field, or a kind this code does not know.</summary>
    public JsonElement? Json(string field) => Fields.TryGetValue(field, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    /// <summary>A reference field of one link, followed.</summary>
    public Linked<T>? Link<T>(string field)
        where T : EntryBase
    {
        if (!Fields.TryGetValue(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            return _pass.Follow<T>(_entry, Where(field), value.GetString()!);
        }
        var what = value.ValueKind == JsonValueKind.Array ? "several links" : $"{Describe(value)}";
        Report(IssueKind.UnexpectedValue, field, $"has {what} in '{Where(field)}', where its type has one");
        return null;
    }

    /// <summary>A reference field of many links, followed, in order.</summary>
    public IReadOnlyList<Linked<T>>? Links<T>(string field)
        where T : EntryBase
    {
        if (!Fields.TryGetValue(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            var what = value.ValueKind == JsonValueKind.String ? "one link" : Describe(value);
            Report(IssueKind.UnexpectedValue, field, $"has {what} in '{Where(field)}', where its type has several");
            return null;
        }
        var links = new List<Linked<T>>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                links.Add(_pass.Follow<T>(_entry, Where(field), item.GetString()!));
            }
            else
            {
                Report(IssueKind.UnexpectedValue, field, $"has {Describe(item)} among the links in '{Where(field)}'");
            }
        }
        return links;
    }

    /// <summary>
    /// An asset field of one asset, as delivery embeds it (doc 40 §14.6). Null when the field
    /// is absent, and when the asset it linked is gone on this ref.
    /// </summary>
    public Asset? Asset(string field) => Value(field, "an asset", JsonValueKind.Object, CohoCms.Delivery.Asset.From);

    /// <summary>An asset field of several assets. A gone asset is left out of the list, never null in it.</summary>
    public IReadOnlyList<Asset>? Assets(string field) =>
        Array(field, "assets", JsonValueKind.Object, value => CohoCms.Delivery.Asset.From(value)!);

    /// <summary>A repeater whose component types are not on the ref: each item untyped, as delivery sent it.</summary>
    public IReadOnlyList<RepeaterItem>? Repeater(string field)
    {
        return Array(field, "repeater items", JsonValueKind.Object, value =>
        {
            var item = ItemReader.SystemKeys(value);
            return new RepeaterItem { Id = item.Id, Type = item.Type, Fields = ItemReader.Own(value) };
        });
    }

    /// <summary>
    /// A repeater's items, in order, each built by <paramref name="read"/> from its component
    /// type. Generated code passes a switch on <see cref="ItemReader.Type"/>. An item it has no
    /// type for, for which it returns null, is left out and reported: the code was generated
    /// before that component was added to the field, and an item is never typed as something
    /// it is not.
    /// </summary>
    public IReadOnlyList<T>? Items<T>(string field, Func<ItemReader, T?> read)
        where T : ItemBase
    {
        ArgumentNullException.ThrowIfNull(read);
        if (!Fields.TryGetValue(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            return Unexpected<List<T>>(field, value, "a list of repeater items");
        }
        var items = new List<T>(value.GetArrayLength());
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var path = $"{Where(field)}/{index++}/";
            if (item.ValueKind != JsonValueKind.Object)
            {
                Report(IssueKind.UnexpectedValue, field, $"has {Describe(item)} in '{Where(field)}', where its type has repeater items");
                continue;
            }
            var reader = new ItemReader(_entry, _pass, item, path);
            if (read(reader) is { } typed)
            {
                items.Add(typed);
                continue;
            }
            var what = reader.Type == "" ? "an item with no type" : $"an item of '{reader.Type}'";
            Report(IssueKind.UnknownItem, field, $"has {what} in '{Where(field)}', which this code has no type for; it is left out");
        }
        return items;
    }

    private T? Value<T>(string field, string expected, JsonValueKind kind, Func<JsonElement, T?> read)
    {
        if (!Fields.TryGetValue(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return default;
        }
        if (value.ValueKind == kind && read(value) is { } result)
        {
            return result;
        }
        return Unexpected<T>(field, value, expected);
    }

    private List<T>? Array<T>(string field, string expected, JsonValueKind kind, Func<JsonElement, T> read)
    {
        if (!Fields.TryGetValue(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            return Unexpected<List<T>>(field, value, $"a list of {expected}");
        }
        var items = new List<T>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == kind)
            {
                items.Add(read(item));
            }
            else
            {
                Report(IssueKind.UnexpectedValue, field, $"has {Describe(item)} in '{Where(field)}', where its type has {expected}");
            }
        }
        return items;
    }

    private T? Unexpected<T>(string field, JsonElement value, string expected)
    {
        Report(IssueKind.UnexpectedValue, field, $"has {Describe(value)} in '{Where(field)}', where its type has {expected}");
        return default;
    }

    /// <summary>The field's place in the entry: its id, or for an item's field, such as <c>citations/0/year</c>, its path.</summary>
    private string Where(string field) => _path + field;

    private void Report(IssueKind kind, string field, string what) => _pass.Issues.Report(kind, _entry, Where(field), what);

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => "text",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "true or false",
        JsonValueKind.Array => "a list",
        JsonValueKind.Object => "an object",
        _ => "an unknown value",
    };
}

/// <summary>One entry's values, read for the record a <see cref="TypeShape.Read"/> builds.</summary>
public sealed class EntryReader : FieldReader
{
    private readonly Entry _entry;

    internal EntryReader(Entry entry, Hydrator.Pass pass)
        : base(entry, pass, entry.Fields, "")
    {
        _entry = entry;
    }

    public string Id => _entry.Id;

    public string Type => _entry.Type;

    public string Slug => _entry.Slug;

    public string? Locale => _entry.Locale;
}

/// <summary>
/// One repeater item's values, read for the record of its component type. Its fields are
/// read as an entry's are; <see cref="Id"/> and <see cref="Type"/> are its system keys.
/// </summary>
public sealed class ItemReader : FieldReader
{
    internal ItemReader(Entry entry, Hydrator.Pass pass, JsonElement item, string path)
        : base(entry, pass, Own(item), path)
    {
        (Id, Type) = SystemKeys(item);
    }

    /// <summary>The item's identity, stable across edits, moves and merges.</summary>
    public string Id { get; }

    /// <summary>The component type's slug: what generated code switches on. Empty when the item has none.</summary>
    public string Type { get; }

    /// <summary>An item's <c>_id</c> and <c>_type</c>, each empty when it has none.</summary>
    internal static (string Id, string Type) SystemKeys(JsonElement item) => (Key(item, "_id"), Key(item, "_type"));

    /// <summary>An item's own fields: everything but its system keys.</summary>
    internal static ImmutableDictionary<string, JsonElement> Own(JsonElement item)
    {
        return item.EnumerateObject()
            .Where(property => property.Name is not ("_id" or "_type"))
            .ToImmutableDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
    }

    private static string Key(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}
