using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace CohoCms.Delivery.Tests;

/// <summary>What generated code declares for the delivery fixture's <c>figure</c> type.</summary>
internal sealed record Figure : EntryBase
{
    public Figure()
    {
    }

    [SetsRequiredMembers]
    private Figure(EntryReader entry)
        : base(entry)
    {
        Caption = entry.Text("caption");
        Image = entry.Asset("image");
        Poster = entry.Asset("poster");
        Gallery = entry.Assets("gallery") ?? [];
    }

    public string? Caption { get; init; }

    public Asset? Image { get; init; }

    public Asset? Poster { get; init; }

    public IReadOnlyList<Asset> Gallery { get; init; } = [];

    public static readonly TypeShape Shape = new() { Read = entry => new Figure(entry) };
}

/// <summary>
/// An entry with assets as delivery 2.1.0 actually sent it (the <c>entry-with-assets</c>
/// recording), hydrated and read through the asset URL builder.
/// </summary>
public sealed class AssetReplayTests
{
    private const string FigureId = "00000000-0000-7000-8000-0000000000f1";
    private const string PhotoId = "00000000-0000-7000-8000-0000000000e1";
    private const string MissingId = "00000000-0000-7000-8000-0000000000ef";

    private static Figure Hydrated() =>
        (Figure)Assert.Single(Hydrator.Hydrate(Spec.Graph("entry-with-assets"), new Dictionary<string, TypeShape> { ["figure"] = Figure.Shape }));

    [Fact]
    public void An_embedded_asset_is_read_with_its_file_and_urls()
    {
        var image = Hydrated().Image!;

        Assert.Equal(PhotoId, image.Id);
        Assert.Equal("A suspension bridge at dusk", image.Alt);
        Assert.Equal(new FocalPoint(0.25, 0.75), image.FocalPoint);
        Assert.Equal(1600, image.File.Width);
        Assert.True(image.IsImage);
        Assert.EndsWith("/Bridge%20at%20dusk.jpg", image.Url);
    }

    [Fact]
    public void A_delivered_image_resizes_and_anything_else_has_only_its_original()
    {
        var gallery = Hydrated().Gallery;

        Assert.EndsWith(
            "?w=400&h=400&fit=fill&fx=0.25&fy=0.75",
            gallery[0].Image(new ImageOptions { Width = 400, Height = 400, Fit = ImageFit.Fill }));
        Assert.Contains(" 1440w", gallery[0].Srcset());
        Assert.DoesNotContain(" 1920w", gallery[0].Srcset());
        Assert.Equal(gallery[1].Url, gallery[1].Image(new ImageOptions { Width = 400 }));
        Assert.Equal(gallery[2].Url, gallery[2].Srcset());
    }

    [Fact]
    public void A_missing_asset_is_null_in_a_single_field_and_left_out_of_a_list()
    {
        var figure = Hydrated();

        Assert.Null(figure.Poster);
        Assert.Equal(
            [PhotoId, "00000000-0000-7000-8000-0000000000e2", "00000000-0000-7000-8000-0000000000e3"],
            figure.Gallery.Select(asset => asset.Id));
    }

    [Fact]
    public void The_graph_carries_the_warnings_and_round_trips_as_delivered()
    {
        var graph = Spec.Graph("entry-with-assets");

        Assert.Equal(
            [
                new DeliveryWarning { Code = "ASSET_MISSING", Entry = FigureId, Field = "gallery", Asset = MissingId },
                new DeliveryWarning { Code = "ASSET_MISSING", Entry = FigureId, Field = "poster", Asset = MissingId },
            ],
            graph.Warnings);
        Assert.True(JsonNode.DeepEquals(Spec.Body("entry-with-assets"), Spec.Json(graph)));
    }
}
