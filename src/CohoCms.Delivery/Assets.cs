using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CohoCms.Delivery;

/// <summary>
/// An asset, as delivery embeds it in an entry's asset field (coho-data doc 40 §14.6): a
/// file's metadata and the URLs the asset CDN serves it at.
/// </summary>
/// <remarks>
/// ⚠️ An asset is as public as its URL, drafts included, and deleting it does not stop
/// its URL working (doc 40 §7.0).
/// </remarks>
public sealed record Asset
{
    /// <summary>The asset's id: what the entry's field links.</summary>
    public string Id { get; init; } = "";

    public string? Title { get; init; }

    /// <summary>Alternative text, for an image.</summary>
    public string? Alt { get; init; }

    /// <summary>Where a crop centres, as fractions of the width and height.</summary>
    public FocalPoint? FocalPoint { get; init; }

    public AssetFile File { get; init; } = new();

    /// <summary>The original bytes, from the asset CDN.</summary>
    public string? Url { get; init; }

    /// <summary>Raster images only: the base that resizes on request. Null for SVG, PDF, audio and video.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Whether the edge resizes this asset: a raster image.</summary>
    public bool IsImage => ImageUrl is not null;

    /// <summary>
    /// The widths the edge serves. A requested width snaps up to the next one, so a
    /// <see cref="Srcset"/> built from these lands on cached images.
    /// </summary>
    public static IReadOnlyList<int> ImageWidths { get; } =
        [16, 32, 48, 64, 96, 128, 192, 256, 320, 384, 480, 640, 750, 828, 960, 1080, 1200, 1440, 1600, 1920, 2048, 2560, 3840];

    /// <summary>
    /// The asset's URL, resized when <paramref name="options"/> asks for it. A file that is
    /// not a raster image has only its original, which is returned whatever the options.
    /// </summary>
    /// <exception cref="InvalidOperationException">The asset has no URL: the deployment serving it has no asset CDN.</exception>
    public string Image(ImageOptions? options = null)
    {
        var query = Query(options ?? new ImageOptions());
        return query.Length == 0 || ImageUrl is null ? Original() : $"{ImageUrl}?{query}";
    }

    /// <summary>
    /// A <c>srcset</c>: one candidate per width, or per pixel density with
    /// <paramref name="densities"/>, which needs <see cref="ImageOptions.Width"/>. Widths
    /// beyond the original are left out, since the edge never enlarges. A file that is not
    /// a raster image has one candidate, its original.
    /// </summary>
    public string Srcset(ImageOptions? options = null, IEnumerable<int>? widths = null, IEnumerable<double>? densities = null)
    {
        options ??= new ImageOptions();
        if (ImageUrl is null)
        {
            return Original();
        }

        double? ratio = options.Width is { } w && options.Height is { } h ? (double)h / w : null;
        string At(int width) => Image(options with
        {
            Width = width,
            Height = ratio is { } r ? (int)Math.Round(width * r) : null,
        });

        if (densities is not null)
        {
            var baseWidth = options.Width ?? throw new ArgumentException("A srcset with densities needs a width.", nameof(options));
            return string.Join(", ", densities.Select(d => $"{At((int)Math.Round(baseWidth * d))} {d.ToString(CultureInfo.InvariantCulture)}x"));
        }

        var largest = File.Width;
        var offered = (widths ?? ImageWidths.Where(each => each >= 640))
            .Distinct()
            .Where(each => largest is null || each <= largest)
            .Order()
            .ToList();
        // An image smaller than every width offered is still offered, at its own size.
        if (offered.Count == 0 && largest is { } own)
        {
            offered.Add(own);
        }
        return string.Join(", ", offered.Select(each => $"{At(each)} {each}w"));
    }

    /// <summary>
    /// A URL that downloads the asset rather than opening it: the original, with <c>dl=1</c>
    /// asking the edge to send it as an attachment under its filename. A browser ignores the
    /// <c>download</c> attribute on a link to another origin, which the asset CDN is, so the
    /// edge has to say it.
    /// </summary>
    /// <remarks>
    /// ⚠️ The edge does not honour <c>dl</c> yet (doc 40 §5.1). Until it does, the file opens
    /// in the browser as <see cref="Url"/> does; the link needs no change when it starts to.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The asset has no URL: the deployment serving it has no asset CDN.</exception>
    public string Download()
    {
        var url = Original();
        return $"{url}{(url.Contains('?', StringComparison.Ordinal) ? '&' : '?')}dl=1";
    }

    private string Query(ImageOptions options)
    {
        var parameters = new List<(string Name, string Value)>();
        void Add(string name, object? value)
        {
            if (value is not null)
            {
                parameters.Add((name, Convert.ToString(value, CultureInfo.InvariantCulture)!));
            }
        }

        Add("w", options.Width);
        Add("h", options.Height);
        Add("fit", options.Fit?.ToString().ToLowerInvariant());
        Add("fm", options.Format?.ToString().ToLowerInvariant());
        Add("q", options.Quality);
        // The edge cannot look the focal point up, so a crop is told where to centre.
        if (FocalPoint is { } focal && options.Fit is ImageFit.Fill or ImageFit.Crop or ImageFit.Thumb)
        {
            Add("fx", Math.Round(focal.X, 2));
            Add("fy", Math.Round(focal.Y, 2));
        }

        var query = new StringBuilder();
        foreach (var (name, value) in parameters)
        {
            query.Append(query.Length == 0 ? "" : "&").Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }
        return query.ToString();
    }

    private string Original() =>
        Url ?? throw new InvalidOperationException("This asset has no URL: the deployment serving it has no asset CDN.");

    /// <summary>Reads an asset as delivery embeds it. Null when the value is not an object.</summary>
    internal static Asset? From(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return new Asset
        {
            Id = Text(value, "id") ?? "",
            Title = Text(value, "title"),
            Alt = Text(value, "alt"),
            FocalPoint = value.TryGetProperty("focalPoint", out var focal) && focal.ValueKind == JsonValueKind.Object
                ? new FocalPoint(Number(focal, "x") ?? 0.5, Number(focal, "y") ?? 0.5)
                : null,
            File = value.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.Object
                ? new AssetFile
                {
                    Filename = Text(file, "filename"),
                    MediaType = Text(file, "mediaType"),
                    ByteSize = Number(file, "byteSize") is { } size ? (long)size : null,
                    Width = Number(file, "width") is { } width ? (int)width : null,
                    Height = Number(file, "height") is { } height ? (int)height : null,
                }
                : new AssetFile(),
            Url = Text(value, "url"),
            ImageUrl = Text(value, "imageUrl"),
        };
    }

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static double? Number(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number ? property.GetDouble() : null;
}

/// <summary>The bytes behind an asset, as the server sniffed them.</summary>
public sealed record AssetFile
{
    public string? Filename { get; init; }

    public string? MediaType { get; init; }

    public long? ByteSize { get; init; }

    /// <summary>Raster images only, as is <see cref="Height"/>.</summary>
    public int? Width { get; init; }

    public int? Height { get; init; }
}

/// <summary>Where a crop centres, as fractions of the image's width and height, 0 to 1.</summary>
public sealed record FocalPoint(double X, double Y);

/// <summary>
/// How to resize an image at the edge. <see cref="ImageFit.Scale"/>, the default, keeps the
/// aspect ratio; <see cref="ImageFit.Fill"/> crops to exactly the size around the focal point.
/// </summary>
public sealed record ImageOptions
{
    public int? Width { get; init; }

    public int? Height { get; init; }

    public ImageFit? Fit { get; init; }

    /// <summary><see cref="ImageFormat.Auto"/> lets the edge pick AVIF, WebP or JPEG from what the browser accepts.</summary>
    public ImageFormat? Format { get; init; }

    /// <summary>1 to 100. The edge rounds to a multiple of 5; its default is 75.</summary>
    public int? Quality { get; init; }

    // ⚠️ There is no Background. A Pad is filled, and a jpg flattened, with a colour the
    // deployment sets rather than one a URL carries. It used to be `bg` here: six hex digits,
    // so about seventeen million cache keys for one image and as many transforms, which anyone
    // could walk. Adding it back means adding that back.
}

public enum ImageFit
{
    Scale,
    Pad,
    Fill,
    Crop,
    Thumb,
}

public enum ImageFormat
{
    Auto,
    Avif,
    Webp,
    Jpg,
    Png,
}
