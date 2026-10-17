using Microsoft.Extensions.Configuration;

namespace CohoCms.Delivery;

/// <summary>
/// A content config from environment variables, so whether an app reads released content
/// or previews unreleased content is a deployment's choice, not a code change.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term><c>COHO_PROJECT</c>, <c>COHO_REF</c></term><description>Always. The ref is a tag or an environment, or with a preview token, a branch or <c>dev</c></description></item>
/// <item><term><c>COHO_LOCALE</c></term><description>Optional</description></item>
/// <item><term><c>COHO_DELIVERY_URL</c>, <c>COHO_DELIVERY_KEY</c></term><description>Released content, from delivery. The key may be left out for a public environment</description></item>
/// <item><term><c>COHO_PREVIEW_TOKEN</c>, <c>COHO_URL</c>, <c>COHO_ACCOUNT</c></term><description>Unreleased content, live through Coho: a project token, Coho's URL, and the project's account</description></item>
/// </list>
/// <para>
/// The names are the same in every Coho SDK, so one deployment's variables serve an app in
/// any language. Read from <see cref="IConfiguration"/>, they can come from the
/// environment, user secrets or <c>appsettings.json</c> alike.
/// </para>
/// <para>
/// A preview token selects a preview. Setting it and a delivery key is refused rather than
/// one quietly winning, since the two read different content. An empty value, such as
/// <c>COHO_DELIVERY_KEY=</c>, counts as unset.
/// </para>
/// </remarks>
public static class CohoEnvironment
{
    /// <summary>From the process's environment variables.</summary>
    public static ContentConfig FromEnvironment() => From(Environment.GetEnvironmentVariable);

    /// <summary>From configuration, under the same flat names.</summary>
    public static ContentConfig FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return From(name => configuration[name]);
    }

    /// <summary>From any lookup of the variables by name.</summary>
    public static ContentConfig From(Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        string? Value(string name)
        {
            var set = lookup(name)?.Trim();
            return string.IsNullOrEmpty(set) ? null : set;
        }

        var token = Value("COHO_PREVIEW_TOKEN");
        var key = Value("COHO_DELIVERY_KEY");
        if (token is not null && key is not null)
        {
            throw new InvalidOperationException("Coho: set COHO_PREVIEW_TOKEN to preview unreleased content, or COHO_DELIVERY_KEY to read released content, not both");
        }

        string[] needed = token is not null
            ? ["COHO_URL", "COHO_ACCOUNT", "COHO_PROJECT", "COHO_REF"]
            : ["COHO_DELIVERY_URL", "COHO_PROJECT", "COHO_REF"];
        var missing = needed.Where(name => Value(name) is null).ToList();
        if (missing.Count > 0)
        {
            var reading = token is not null ? "a preview, since COHO_PREVIEW_TOKEN is set" : "released content";
            throw new InvalidOperationException($"Coho: {string.Join(", ", missing)} {(missing.Count == 1 ? "is" : "are")} needed to read {reading}");
        }

        // Checked present above, so each `!` names a variable the list required.
        DeliveryConfig delivery = token is not null
            ? new PreviewConfig { Url = Value("COHO_URL")!, Account = Value("COHO_ACCOUNT")!, Project = Value("COHO_PROJECT")!, Token = token }
            : new ReleasedConfig { Url = Value("COHO_DELIVERY_URL")!, Project = Value("COHO_PROJECT")!, Key = key };
        return new ContentConfig { Delivery = delivery, Ref = Value("COHO_REF")!, Locale = Value("COHO_LOCALE") };
    }
}
