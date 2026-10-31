using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CohoCms.Delivery.Tests;

/// <summary>What generated code declares for the delivery fixture's <c>step</c> component, whose label is localized.</summary>
internal sealed record Step : ItemBase
{
    public Step()
    {
    }

    [SetsRequiredMembers]
    internal Step(ItemReader item)
        : base(item)
    {
        Label = item.Text("label");
        Minutes = item.Integer("minutes");
    }

    public string? Label { get; init; }

    public long? Minutes { get; init; }
}

/// <summary>
/// What generated code declares for the delivery fixture's <c>figure</c> type, with its lists,
/// and lists of numbers and of date-times beside them, which the fixture does not have.
/// </summary>
internal sealed record ListedFigure : EntryBase
{
    public ListedFigure()
    {
    }

    [SetsRequiredMembers]
    private ListedFigure(EntryReader entry)
        : base(entry)
    {
        Steps = entry.Items<Step>("steps", static each => each.Type switch { "step" => new Step(each), _ => null }) ?? [];
        Keywords = entry.Texts("keywords");
        Sizes = entry.Integers("sizes") ?? [];
        Scores = entry.Numbers("scores") ?? [];
        Sessions = entry.DateTimes("sessions") ?? [];
    }

    public IReadOnlyList<Step> Steps { get; init; } = [];

    /// <summary>Localized: null where the locale has no translation, and empty where it has none.</summary>
    public IReadOnlyList<string>? Keywords { get; init; }

    public IReadOnlyList<long> Sizes { get; init; } = [];

    public IReadOnlyList<double> Scores { get; init; } = [];

    public IReadOnlyList<DateTimeOffset> Sessions { get; init; } = [];

    public static readonly TypeShape Shape = new() { Read = entry => new ListedFigure(entry) };
}

/// <summary>
/// Lists of plain values, and localized fields inside repeater items, as delivery actually sent
/// them (the <c>entry-with-localized-items</c> recording), hydrated through the records generated
/// code writes.
/// </summary>
public sealed class ListReplayTests
{
    private const string FigureId = "00000000-0000-7000-8000-0000000000f3";

    private static readonly Dictionary<string, TypeShape> Shapes = new() { ["figure"] = ListedFigure.Shape };

    /// <summary>The recording, with <paramref name="fields"/> set on the figure.</summary>
    private static EntryList With(params (string Field, JsonNode? Value)[] fields)
    {
        var body = Spec.Body("entry-with-localized-items").DeepClone();
        foreach (var (field, value) in fields)
        {
            body["entries"]![FigureId]!["fields"]![field] = value;
        }
        return body.Deserialize(DeliveryJson.Default.EntryList)!;
    }

    [Fact]
    public void A_list_is_read_as_its_values_and_a_locales_empty_list_as_empty()
    {
        var issues = new List<Issue>();

        var figure = (ListedFigure)Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-with-localized-items"), Shapes, issues.Add));

        Assert.Equal([3L, 5L], figure.Sizes);
        // Sent as [], the value German has: none.
        Assert.NotNull(figure.Keywords);
        Assert.Empty(figure.Keywords);
        Assert.Empty(issues);
    }

    [Fact]
    public void A_list_delivery_leaves_out_is_null_to_its_reader()
    {
        var issues = new List<Issue>();

        var figure = (ListedFigure)Assert.Single(Hydrator.Hydrate(With(("keywords", null)), Shapes, issues.Add));

        // A null and a left-out field read alike; the record decides what absent is.
        Assert.Null(figure.Keywords);
        Assert.Empty(figure.Scores);
        Assert.Empty(issues);
    }

    [Fact]
    public void A_localized_field_in_an_item_is_the_locales_value_or_absent()
    {
        var figure = (ListedFigure)Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-with-localized-items"), Shapes));

        Assert.Equal(["Messen", null], figure.Steps.Select(step => step.Label));
        Assert.Equal([5L, 2L], figure.Steps.Select(step => step.Minutes));
        Assert.Equal(["00000000-0000-7000-8000-0000000000a3", "00000000-0000-7000-8000-0000000000a4"], figure.Steps.Select(step => step.Id));
    }

    [Fact]
    public void Lists_of_numbers_and_of_date_times_are_read_as_their_values()
    {
        var figure = (ListedFigure)Assert.Single(Hydrator.Hydrate(
            With(("scores", new JsonArray(1.5, 2)), ("sessions", new JsonArray("2026-10-31T09:00:00Z", "2026-11-01T09:00:00+01:00"))),
            Shapes));

        Assert.Equal([1.5, 2.0], figure.Scores);
        Assert.Equal([new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(1))], figure.Sessions);
    }

    [Fact]
    public void A_value_of_the_wrong_kind_in_a_list_is_reported_and_left_out()
    {
        var issues = new List<Issue>();

        var figure = (ListedFigure)Assert.Single(Hydrator.Hydrate(
            With(("sizes", new JsonArray(3, 4.5, "5")), ("sessions", new JsonArray("2026-10-31T09:00:00Z", "soon")), ("keywords", "one")),
            Shapes,
            issues.Add));

        Assert.Equal([3L], figure.Sizes);
        Assert.Single(figure.Sessions);
        Assert.Null(figure.Keywords);
        Assert.Equal(["keywords", "sessions", "sizes"], issues.Select(issue => issue.Field).Order(StringComparer.Ordinal));
        Assert.All(issues, issue => Assert.Equal(IssueKind.UnexpectedValue, issue.Kind));
        Assert.Contains(issues, issue => issue.Message.Contains("where its type has integers", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Message.Contains("where its type has a list of text", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_record_a_list_is_its_values_as_delivery_sent_them_never_a_link()
    {
        var figure = (HydratedEntry)Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-with-localized-items")));

        Assert.Equal([3L, 5L], figure.Field("sizes")!.Value.EnumerateArray().Select(each => each.GetInt64()));
        Assert.Equal(JsonValueKind.Array, figure.Field("keywords")?.ValueKind);
        Assert.Null(figure.Links("sizes"));
    }
}
