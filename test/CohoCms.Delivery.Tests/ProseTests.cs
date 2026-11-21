using System.Diagnostics.CodeAnalysis;

namespace CohoCms.Delivery.Tests;

/// <summary>
/// Resolving what a body points at (coho-data doc 80 ML-1), against the three shapes the
/// portal writes, and leaving everything else — a reference nothing can place included —
/// exactly as it was. The same cases as <c>@coho-cms/delivery</c>'s <c>prose.test.ts</c>,
/// because the rule has to be one rule.
/// </summary>
public sealed class ProseTests
{
    private static readonly Asset Bridge = new()
    {
        Id = "e1",
        Slug = "bridge-at-dusk",
        Url = "https://assets.test/a/h1/bridge.jpg",
    };

    private static readonly Asset Tour = new()
    {
        Id = "e2",
        Slug = "ring-road",
        Url = "https://assets.test/a/h2/tour.mp4",
    };

    private static readonly Asset Sheet = new()
    {
        Id = "e3",
        Slug = "datasheet-2026",
        Url = "https://assets.test/a/h3/d.pdf",
    };

    private static readonly Asset[] Assets = [Bridge, Tour, Sheet];

    [Fact]
    public void AnImageAVideoDirectiveAndADownloadLinkEachResolve()
    {
        Assert.Equal($"![A bridge]({Bridge.Url})", Prose.Resolve("![A bridge](asset:bridge-at-dusk)", Assets));
        Assert.Equal($"::video[The tour]{{src=\"{Tour.Url}\"}}", Prose.Resolve("::video[The tour]{src=\"asset:ring-road\"}", Assets));
        Assert.Equal($"[The datasheet]({Sheet.Url})", Prose.Resolve("[The datasheet](asset:datasheet-2026)", Assets));
    }

    [Fact]
    public void AMarkdownTitleSurvivesAndAnHtmlAttributeResolvesToo()
    {
        Assert.Equal($"![A bridge]({Bridge.Url} \"At dusk\")", Prose.Resolve("![A bridge](asset:bridge-at-dusk \"At dusk\")", Assets));
        Assert.Equal($"<img src=\"{Bridge.Url}\">", Prose.Resolve("<img src=\"asset:bridge-at-dusk\">", Assets));
        Assert.Equal($"<video poster='{Bridge.Url}'>", Prose.Resolve("<video poster='asset:bridge-at-dusk'>", Assets));
    }

    [Fact]
    public void OnlyAnAttributeThatFetchesAndOnlyWhenTheValueIsTheWholeReference()
    {
        // `alt` fetches nothing, and a URL with the text inside it is not a reference.
        Assert.Equal("<img alt=\"asset:bridge-at-dusk\">", Prose.Resolve("<img alt=\"asset:bridge-at-dusk\">", Assets));
        Assert.Equal("[x](https://other.test/asset:bridge-at-dusk)", Prose.Resolve("[x](https://other.test/asset:bridge-at-dusk)", Assets));

        // Bare text is not a reference either, so a body can write about the scheme.
        Assert.Equal("Write asset:bridge-at-dusk to name it.", Prose.Resolve("Write asset:bridge-at-dusk to name it.", Assets));
    }

    [Fact]
    public void AReferenceTheEntryDoesNotCarryIsLeftExactlyAsWritten()
    {
        Assert.Equal("![Not here](asset:not-here-yet)", Prose.Resolve("![Not here](asset:not-here-yet)", Assets));

        // An asset with no URL cannot be resolved to either.
        Asset pending = new() { Id = "e4", Slug = "pending" };
        Assert.Equal("![A](asset:pending)", Prose.Resolve("![A](asset:pending)", [pending]));
    }

    [Fact]
    public void AnEntryResolvesAgainstTheAssetsItCarries()
    {
        var entry = new Article
        {
            Id = "p1",
            Type = "article",
            Slug = "a-bridge",
            Body = "![A bridge](asset:bridge-at-dusk) and [the sheet](asset:datasheet-2026).",
            BodyAssets = Assets,
        };

        Assert.Equal($"![A bridge]({Bridge.Url}) and [the sheet]({Sheet.Url}).", Prose.Resolve(entry.Body, entry));
    }

    [Fact]
    public void AnEntryThatNamesNothingLeavesEveryReferenceStanding()
    {
        var entry = new Article { Id = "p2", Type = "article", Slug = "plain", Body = "![A bridge](asset:bridge-at-dusk)" };

        Assert.Equal("![A bridge](asset:bridge-at-dusk)", Prose.Resolve(entry.Body, entry));
    }

    [Fact]
    public void ABodyThatIsNotThereComesBackAsItWas()
    {
        Assert.Null(Prose.Resolve(null, Assets));
        Assert.Equal("", Prose.Resolve("", Assets));
        Assert.Equal("Plain prose.", Prose.Resolve("Plain prose.", Assets));
    }

    [Fact]
    public void AReferenceReadsBackAsTheSlugItNames()
    {
        Assert.Equal("asset:bridge-at-dusk", Prose.Reference("bridge-at-dusk"));
        Assert.Equal("bridge-at-dusk", Prose.ReferencedSlug("asset:bridge-at-dusk"));
        Assert.Null(Prose.ReferencedSlug("https://assets.test/a/h1/bridge.jpg"));
        Assert.Null(Prose.ReferencedSlug("asset:"));
        Assert.Null(Prose.ReferencedSlug("asset:not a slug"));
        Assert.Null(Prose.ReferencedSlug(null));
    }

    /// <summary>What generated code declares for a type with a prose field.</summary>
    private sealed record Article : EntryBase
    {
        public Article()
        {
        }

        [SetsRequiredMembers]
        private Article(EntryReader entry)
            : base(entry) => Body = entry.Text("body");

        public string? Body { get; init; }
    }
}
