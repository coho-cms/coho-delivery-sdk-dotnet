using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CohoCms.Delivery;

/// <summary>
/// Where a client reads, and what authorizes it: <see cref="ReleasedConfig"/>,
/// <see cref="PreviewConfig"/> or <see cref="MountedConfig"/>.
/// </summary>
/// <remarks>
/// A credential is sent as <c>Authorization: Bearer</c>, never in a URL, and never
/// printed: <c>ToString()</c> shows only whether one is set. Keep it on the server.
/// </remarks>
public abstract record DeliveryConfig
{
    private protected DeliveryConfig()
    {
    }

    /// <summary>The URL every read is relative to: <c>…/v1/projects/{project}</c>, or a preview's mount.</summary>
    internal abstract string Base { get; }

    /// <summary>The key or token, if any.</summary>
    internal abstract string? Credential { get; }

    internal abstract Reads Reads { get; }

    internal static string Trim(string url) => url.TrimEnd('/');
}

/// <summary>
/// Released content: tags, and environments pointing at them, such as <c>qa</c> and
/// <c>prod</c>, from delivery, with a delivery key.
/// </summary>
public sealed record ReleasedConfig : DeliveryConfig
{
    /// <summary>Where delivery is served, such as <c>https://delivery.coho.example</c>.</summary>
    public required string Url { get; init; }

    /// <summary>The project id. It selects the project; the key is what authorizes.</summary>
    public required string Project { get; init; }

    /// <summary>
    /// A delivery key. Leave it out only for an environment the project has opened to
    /// keyless reads.
    /// </summary>
    public string? Key { get; init; }

    internal override string Base => $"{Trim(Url)}/v1/projects/{Uri.EscapeDataString(Project)}";

    internal override string? Credential => Key;

    internal override Reads Reads => Reads.Released;

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Url = {Url}, Project = {Project}, Key = {(Key is null ? "none" : "set")}");
        return true;
    }
}

/// <summary>
/// Unreleased content: <c>dev</c> and branch heads, read live through Coho with a
/// project token. The same answers in the same shapes as delivery, never cached, so a
/// preview runs the code production runs.
/// </summary>
public sealed record PreviewConfig : DeliveryConfig
{
    /// <summary>
    /// The project token prefix, by which Coho tells a token from a person's sign-in.
    /// </summary>
    public const string TokenPrefix = "coho_pt_";

    /// <summary>Coho's own URL, where you sign in, such as <c>https://api.coho.example</c>. Not delivery's.</summary>
    public required string Url { get; init; }

    /// <summary>The account the project belongs to.</summary>
    public required string Account { get; init; }

    public required string Project { get; init; }

    /// <summary>
    /// A project token, <c>coho_pt_…</c>, with read access to the project: the viewer role
    /// is enough. It reads everything unreleased, so keep it on the server, out of
    /// anything a browser loads.
    /// </summary>
    public required string Token { get; init; }

    internal override string Base =>
        $"{Trim(Url)}/api/v1/accounts/{Uri.EscapeDataString(Account)}/projects/{Uri.EscapeDataString(Project)}/preview";

    internal override string? Credential => Token;

    internal override Reads Reads => Reads.Preview;

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Url = {Url}, Account = {Account}, Project = {Project}, Token = set");
        return true;
    }
}

/// <summary>
/// The whole base URL, <c>…/v1/projects/{project}</c>, for a mount that
/// <see cref="ReleasedConfig"/> cannot express, such as a proxy.
/// </summary>
public sealed record MountedConfig : DeliveryConfig
{
    public required string BaseUrl { get; init; }

    /// <summary>A delivery key.</summary>
    public string? Key { get; init; }

    internal override string Base => Trim(BaseUrl);

    internal override string? Credential => Key;

    internal override Reads Reads => Reads.Released;

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"BaseUrl = {BaseUrl}, Key = {(Key is null ? "none" : "set")}");
        return true;
    }
}

/// <summary>Released content, from delivery; or unreleased, read live through Coho's preview mount.</summary>
internal enum Reads
{
    Released,
    Preview,
}

/// <summary>What a client is built with, however it reaches Coho.</summary>
public sealed record ClientOptions
{
    /// <summary>
    /// The client to send requests with. Without it, and without
    /// <see cref="HttpClientFactory"/>, a client shared by every Coho client is used.
    /// </summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>
    /// Where to get a client for each request, so its handlers are pooled and rotated.
    /// Set by <c>AddCohoContent</c> and <c>AddCohoDelivery</c>.
    /// </summary>
    public IHttpClientFactory? HttpClientFactory { get; init; }

    /// <summary>The name of the client <see cref="HttpClientFactory"/> creates.</summary>
    public string HttpClientName { get; init; } = CohoServiceCollectionExtensions.HttpClientName;

    /// <summary>
    /// The private response cache: on by default, holding up to 1000 responses. 0
    /// turns it off.
    /// </summary>
    public int MaxCachedResponses { get; init; } = 1000;

    /// <summary>The clock the cache's freshness is measured by.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Called once per hydration issue, for the life of a content client. Without it,
    /// issues are logged to <see cref="Logger"/> as warnings, or without a logger,
    /// written to standard error.
    /// </summary>
    public Action<Issue>? OnIssue { get; init; }

    public ILogger? Logger { get; init; }
}
