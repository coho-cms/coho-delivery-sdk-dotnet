using System.Text.Json;

namespace CohoCms.Delivery.Tests;

/// <summary>
/// An asset as delivery embeds it (coho-data doc 40 §14.6), and the URLs built from it:
/// the same rules as <c>@coho-cms/delivery</c>'s <c>imageUrl</c> and <c>srcset</c>.
/// </summary>
public sealed class AssetTests
{
    private const string Base = "https://assets.example.test/p1";

    private static readonly Asset Photo = new()
    {
        Id = "a1",
        Alt = "The bridge",
        FocalPoint = new FocalPoint(0.25, 0.75),
        File = new AssetFile { MediaType = "image/jpeg", Width = 1200, Height = 800, Filename = "bridge.jpg" },
        Url = $"{Base}/a/abc/bridge.jpg",
        ImageUrl = $"{Base}/i/abc/bridge.jpg",
    };

    private static readonly Asset Logo = new()
    {
        Id = "a2",
        File = new AssetFile { MediaType = "image/svg+xml", Filename = "logo.svg" },
        Url = $"{Base}/a/def/logo.svg",
    };

    [Fact]
    public void An_embedded_asset_is_read_with_its_file_and_urls()
    {
        using var json = JsonDocument.Parse("""
            {"id":"a1","title":"Bridge","alt":"The bridge","focalPoint":{"x":0.25,"y":0.75},
             "file":{"mediaType":"image/jpeg","byteSize":81234,"width":1200,"height":800,"filename":"bridge.jpg"},
             "url":"https://assets.example.test/p1/a/abc/bridge.jpg","imageUrl":"https://assets.example.test/p1/i/abc/bridge.jpg"}
            """);
        var asset = Asset.From(json.RootElement)!;

        Assert.Equal("a1", asset.Id);
        Assert.Equal("The bridge", asset.Alt);
        Assert.Equal(new FocalPoint(0.25, 0.75), asset.FocalPoint);
        Assert.Equal(1200, asset.File.Width);
        Assert.Equal(81234, asset.File.ByteSize);
        Assert.True(asset.IsImage);
        Assert.Null(Asset.From(JsonDocument.Parse("\"a1\"").RootElement));
    }

    [Fact]
    public void An_image_resizes_with_a_query_in_a_fixed_order()
    {
        Assert.Equal($"{Base}/a/abc/bridge.jpg", Photo.Image());
        Assert.Equal(
            $"{Base}/i/abc/bridge.jpg?w=800&fm=auto",
            Photo.Image(new ImageOptions { Format = ImageFormat.Auto, Width = 800 }));
    }

    [Fact]
    public void A_crop_centres_on_the_focal_point_and_a_scale_does_not()
    {
        Assert.Equal(
            $"{Base}/i/abc/bridge.jpg?w=400&h=400&fit=fill&fx=0.25&fy=0.75",
            Photo.Image(new ImageOptions { Width = 400, Height = 400, Fit = ImageFit.Fill }));
        Assert.DoesNotContain("fx=", Photo.Image(new ImageOptions { Width = 400, Fit = ImageFit.Scale }));
    }

    [Fact]
    public void A_file_that_is_not_a_raster_image_has_only_its_original()
    {
        Assert.Equal($"{Base}/a/def/logo.svg", Logo.Image(new ImageOptions { Width = 800 }));
        Assert.Equal($"{Base}/a/def/logo.svg", Logo.Srcset());
        Assert.False(Logo.IsImage);
    }

    [Fact]
    public void A_srcset_offers_the_edge_widths_up_to_the_original()
    {
        Assert.Equal(
            $"{Base}/i/abc/bridge.jpg?w=640 640w, {Base}/i/abc/bridge.jpg?w=750 750w, {Base}/i/abc/bridge.jpg?w=828 828w, " +
                $"{Base}/i/abc/bridge.jpg?w=960 960w, {Base}/i/abc/bridge.jpg?w=1080 1080w, {Base}/i/abc/bridge.jpg?w=1200 1200w",
            Photo.Srcset());
    }

    [Fact]
    public void A_srcset_by_density_keeps_the_aspect_ratio()
    {
        Assert.Equal(
            $"{Base}/i/abc/bridge.jpg?w=300&h=200 1x, {Base}/i/abc/bridge.jpg?w=600&h=400 2x",
            Photo.Srcset(new ImageOptions { Width = 300, Height = 200 }, densities: [1, 2]));
        Assert.Throws<ArgumentException>(() => Photo.Srcset(densities: [1, 2]));
    }

    [Fact]
    public void A_download_asks_the_edge_for_an_attachment_of_any_file()
    {
        Assert.Equal($"{Base}/a/abc/bridge.jpg?dl=1", Photo.Download());
        Assert.Equal($"{Base}/a/def/logo.svg?dl=1", Logo.Download());
        Assert.Equal($"{Base}/a/abc/x.pdf?v=2&dl=1", (Logo with { Url = $"{Base}/a/abc/x.pdf?v=2" }).Download());
    }

    [Fact]
    public void An_asset_with_no_url_says_why()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new Asset().Image());
        Assert.Contains("no asset CDN", error.Message);
        Assert.Throws<InvalidOperationException>(() => new Asset().Download());
    }
}
