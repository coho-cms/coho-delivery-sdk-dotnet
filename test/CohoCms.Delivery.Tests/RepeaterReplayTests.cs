using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CohoCms.Delivery.Tests;

/// <summary>What generated code declares for the delivery fixture's <c>citation</c> component.</summary>
internal sealed record Citation : ItemBase
{
    public Citation()
    {
    }

    [SetsRequiredMembers]
    internal Citation(ItemReader item)
        : base(item)
    {
        Authors = item.Text("authors") ?? "";
        Year = item.Integer("year") ?? default;
        Url = item.Text("url");
    }

    public string Authors { get; init; } = "";

    public long Year { get; init; }

    public string? Url { get; init; }
}

/// <summary>What generated code declares for the delivery fixture's <c>figure</c> type, with its repeater.</summary>
internal sealed record SourcedFigure : EntryBase
{
    public SourcedFigure()
    {
    }

    [SetsRequiredMembers]
    private SourcedFigure(EntryReader entry)
        : base(entry)
    {
        Sources = entry.Items<Citation>("sources", static each => each.Type switch { "citation" => new Citation(each), _ => null }) ?? [];
    }

    public IReadOnlyList<Citation> Sources { get; init; } = [];

    public static readonly TypeShape Shape = new() { Read = entry => new SourcedFigure(entry) };
}

/// <summary>
/// An entry with repeater items as delivery actually sent it (the <c>entry-with-repeater</c>
/// recording), hydrated through the records generated code writes.
/// </summary>
public sealed class RepeaterReplayTests
{
    private const string FigureId = "00000000-0000-7000-8000-0000000000f2";

    private static readonly Dictionary<string, TypeShape> Shapes = new() { ["figure"] = SourcedFigure.Shape };

    [Fact]
    public void Repeater_items_arrive_as_stored_each_with_its_id_and_type()
    {
        var figure = (SourcedFigure)Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-with-repeater"), Shapes));

        Assert.Equal(2, figure.Sources.Count);
        var (first, second) = (figure.Sources[0], figure.Sources[1]);
        Assert.Equal("00000000-0000-7000-8000-0000000000a1", first.Id);
        Assert.Equal("citation", first.Type);
        Assert.Equal(2022, first.Year);
        Assert.Equal("https://doi.org/10.1061/9780784415788", first.Url);
        // Sent without a _type; the server filled it in, since sources holds one component.
        Assert.Equal("citation", second.Type);
        Assert.Null(second.Url);
    }

    [Fact]
    public void An_item_of_a_component_the_code_has_no_type_for_is_left_out_and_reported_once()
    {
        var body = Spec.Body("entry-with-repeater").DeepClone();
        var sources = body["entries"]![FigureId]!["fields"]!["sources"]!.AsArray();
        sources.Add(new JsonObject { ["_id"] = "x1", ["_type"] = "video" });
        sources.Add(new JsonObject { ["_id"] = "x2", ["_type"] = "video" });
        sources.Add(new JsonObject { ["_id"] = "x3" });
        var graph = body.Deserialize(DeliveryJson.Default.EntryList)!;

        var issues = new List<Issue>();
        var figure = (SourcedFigure)Assert.Single(Hydrator.Hydrate(graph, Shapes, issues.Add));

        Assert.Equal(["00000000-0000-7000-8000-0000000000a1", "00000000-0000-7000-8000-0000000000a2"], figure.Sources.Select(each => each.Id));
        var issue = Assert.Single(issues);
        Assert.Equal((IssueKind.UnknownItem, "sources"), (issue.Kind, issue.Field));
        Assert.Contains("an item of 'video'", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_items_field_of_the_wrong_kind_is_reported_at_its_place_in_the_list()
    {
        var body = Spec.Body("entry-with-repeater").DeepClone();
        body["entries"]![FigureId]!["fields"]!["sources"]![1]!["year"] = "2010";
        var graph = body.Deserialize(DeliveryJson.Default.EntryList)!;

        var issues = new List<Issue>();
        var figure = (SourcedFigure)Assert.Single(Hydrator.Hydrate(graph, Shapes, issues.Add));

        Assert.Equal(0, figure.Sources[1].Year);
        var issue = Assert.Single(issues);
        Assert.Equal((IssueKind.UnexpectedValue, "sources/1/year"), (issue.Kind, issue.Field));
    }

    [Fact]
    public void Without_a_record_a_repeater_is_read_untyped_with_each_items_type()
    {
        var figure = (HydratedEntry)Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-with-repeater")));

        Assert.Equal(2, figure.Field("sources")?.GetArrayLength());
    }
}
