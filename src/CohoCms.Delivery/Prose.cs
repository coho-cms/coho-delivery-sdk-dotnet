using System.Text.RegularExpressions;

namespace CohoCms.Delivery;

/// <summary>
/// Turning an <c>asset:</c> reference in a body into the file it names (coho-data doc 80 ML-1).
/// </summary>
/// <remarks>
/// A Markdown or HTML field names an asset by <b>slug</b> — <c>![A bridge](asset:bridge-at-dusk)</c>
/// — rather than by URL, so the body carries no asset domain, survives being moved between
/// branches, and is seen by everything that counts a file as used. Nothing renders that on its
/// own: Coho records what the prose named in the entry's <see cref="EntryBase.BodyAssets"/>,
/// delivery embeds those assets as it embeds any asset field, and this turns one into the other.
/// <para>
/// It is here rather than in each application because the rule is not obvious and has to match
/// the server's: several copies of these patterns already exist, and every consumer that renders
/// a body would otherwise write another.
/// </para>
/// <para>
/// ⚠️ <b>Resolve at the read, and never before a write.</b> Call this where an entry arrives,
/// once, rather than at each place a body is drawn — a body usually reaches more than one
/// renderer, and the version that none of them can forget is the one at the read.
/// </para>
/// <para>
/// ⛔ <b>Never resolve a body you may write back.</b> A resolved body holds URLs, and saving one
/// destroys the reference the scheme exists to keep: the asset stops counting as used, the link
/// breaks when the file is replaced, and moving the entry to another project carries the old
/// domain with it. Anything that edits content must keep the body exactly as delivered — which
/// is why resolving is a call you make and not something a read does for you.
/// </para>
/// <para>
/// ⚠️ A reference to an asset the entry does not carry is <b>left exactly as written</b>. That is
/// an asset not merged onto this ref, or a slug typed by hand; an image then falls back to its
/// alt text. Rewriting it to nothing would hide it, and throwing would stop a page over one figure.
/// </para>
/// </remarks>
public static partial class Prose
{
    /// <summary>The scheme a body names an asset by.</summary>
    public const string Scheme = "asset:";

    /// <summary>The key delivery sends an entry's prose assets under (coho-data doc 80 §10).</summary>
    public const string BodyAssetsField = "_bodyAssets";

    /// <summary>What a body writes to name the asset with this slug.</summary>
    public static string Reference(string slug) => Scheme + slug;

    /// <summary>
    /// The slug <paramref name="value"/> references, or null when it is an ordinary URL or
    /// anything else. A caller that wants to find references rather than replace them.
    /// </summary>
    public static string? ReferencedSlug(string? value)
    {
        if (value is null || !value.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return null;
        }

        var slug = value[Scheme.Length..];
        return Slug().IsMatch(slug) ? slug : null;
    }

    /// <summary>
    /// <paramref name="text"/> with every <c>asset:</c> reference the entry carries replaced by
    /// that asset's URL: the overload a renderer holding a typed entry wants.
    /// </summary>
    public static string? Resolve(string? text, EntryBase entry) =>
        Resolve(text, (entry ?? throw new ArgumentNullException(nameof(entry))).BodyAssets);

    /// <summary>
    /// <paramref name="text"/> with every <c>asset:</c> reference <paramref name="assets"/> can
    /// place replaced by that asset's URL. An asset with no slug or no URL cannot be resolved to,
    /// and a reference to one is left as written.
    /// </summary>
    public static string? Resolve(string? text, IEnumerable<Asset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (text is null || !text.Contains(Scheme, StringComparison.Ordinal))
        {
            return text;
        }

        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            if (asset is { Slug: { Length: > 0 } slug, Url: { Length: > 0 } url })
            {
                urls[slug] = url;
            }
        }

        if (urls.Count == 0)
        {
            return text;
        }

        string Swap(Match match) =>
            urls.TryGetValue(match.Groups[2].Value, out var url)
                ? match.Groups[1].Value + url + match.Groups[3].Value
                : match.Value;

        return Attribute().Replace(Target().Replace(text, Swap), Swap);
    }

    /// <summary>A slug's shape, as the server reads one. These must agree.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex Slug();

    /// <summary>
    /// <c>![alt](asset:slug)</c> and <c>[label](asset:slug)</c>, with Markdown's optional title
    /// after it. Bare text is deliberately not a reference, so a body can write about the scheme.
    /// </summary>
    [GeneratedRegex(@"(!?\[[^\]]*\]\(\s*)asset:([A-Za-z0-9][A-Za-z0-9._-]*)(\s*(?:""[^""]*"")?\s*\))")]
    private static partial Regex Target();

    /// <summary>
    /// <c>src</c>, <c>href</c> or <c>poster</c> naming an asset: a video directive's attribute in
    /// Markdown, or an attribute in an HTML field. Only attributes that fetch something, and only
    /// when the whole value is the reference — so <c>alt="asset:x"</c> and a URL with the text
    /// inside it are not mistaken for one.
    /// </summary>
    [GeneratedRegex(@"(\b(?:src|href|poster)\s*=\s*[""'])asset:([A-Za-z0-9][A-Za-z0-9._-]*)([""'])")]
    private static partial Regex Attribute();
}
