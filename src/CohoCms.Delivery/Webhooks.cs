using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace CohoCms.Delivery;

/// <summary>
/// Checks that a webhook request came from Coho, and reads its event.
/// </summary>
/// <remarks>
/// Coho signs each request as Standard Webhooks does: HMAC-SHA256 with the endpoint's secret
/// over <c>{webhook-id}.{webhook-timestamp}.{body}</c>, written <c>v1,…</c> in
/// <c>webhook-signature</c>. While a secret is being rotated, the header carries a signature
/// for each.
/// </remarks>
public static class Webhooks
{
    /// <summary>How far <c>webhook-timestamp</c> may be from the receiver's clock, either way: five minutes.</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300);

    private const string SecretPrefix = "whsec_";

    /// <summary>The event a request carries, once its signature and timestamp are checked.</summary>
    /// <param name="body">
    /// The body exactly as it arrived. Never parse and re-serialize it first: the signature is
    /// over the bytes Coho sent.
    /// </param>
    /// <param name="headers">The request's headers, such as ASP.NET Core's <c>Request.Headers</c>. Names are matched ignoring case.</param>
    /// <param name="secret">The endpoint's signing secret, <c>whsec_…</c>.</param>
    /// <param name="now">The receiver's time. Pass one only in tests.</param>
    /// <param name="tolerance">How far <c>webhook-timestamp</c> may be from <paramref name="now"/>, either way. Five minutes unless given.</param>
    /// <exception cref="CohoWebhookException">The request is refused; <see cref="CohoWebhookException.Reason"/> says why.</exception>
    /// <exception cref="ArgumentException">A secret is not <c>whsec_</c> and base64.</exception>
    /// <exception cref="JsonException">
    /// A body that verifies is not a Coho event, which only a sender holding the secret could make.
    /// </exception>
    public static WebhookEvent Verify(
        ReadOnlySpan<byte> body,
        IEnumerable<KeyValuePair<string, StringValues>> headers,
        string secret,
        DateTimeOffset? now = null,
        TimeSpan? tolerance = null)
    {
        return Verify(body, headers, [secret], now, tolerance);
    }

    /// <summary>The same, accepting a request signed with any of <paramref name="secrets"/>: both, while one is rotated.</summary>
    public static WebhookEvent Verify(
        ReadOnlySpan<byte> body,
        IEnumerable<KeyValuePair<string, StringValues>> headers,
        IEnumerable<string> secrets,
        DateTimeOffset? now = null,
        TimeSpan? tolerance = null)
    {
        Check(body, headers, secrets, now ?? DateTimeOffset.UtcNow, tolerance ?? DefaultTolerance);
        return WebhookEvent.Parse(body);
    }

    /// <summary>The same, for a body read as text. It is checked as its UTF-8 bytes.</summary>
    public static WebhookEvent Verify(
        string body,
        IEnumerable<KeyValuePair<string, StringValues>> headers,
        string secret,
        DateTimeOffset? now = null,
        TimeSpan? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Verify(Encoding.UTF8.GetBytes(body), headers, [secret], now, tolerance);
    }

    /// <summary>The same, for a body read as text, signed with any of <paramref name="secrets"/>.</summary>
    public static WebhookEvent Verify(
        string body,
        IEnumerable<KeyValuePair<string, StringValues>> headers,
        IEnumerable<string> secrets,
        DateTimeOffset? now = null,
        TimeSpan? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Verify(Encoding.UTF8.GetBytes(body), headers, secrets, now, tolerance);
    }

    /// <summary>Refuses a request whose headers, timestamp, or signature do not check out; reads nothing.</summary>
    internal static void Check(
        ReadOnlySpan<byte> body,
        IEnumerable<KeyValuePair<string, StringValues>> headers,
        IEnumerable<string> secrets,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(secrets);
        var keys = secrets.Select(Key).ToList();
        if (keys.Count == 0)
        {
            throw new ArgumentException("A webhook needs a secret to verify it with", nameof(secrets));
        }

        var id = Header(headers, "webhook-id");
        var timestamp = Header(headers, "webhook-timestamp");
        var signature = Header(headers, "webhook-signature");
        if (id is null || timestamp is null || signature is null)
        {
            var missing = new[] { ("webhook-id", id), ("webhook-timestamp", timestamp), ("webhook-signature", signature) }
                .Where(each => each.Item2 is null)
                .Select(each => each.Item1);
            throw new CohoWebhookException(WebhookRefusal.Headers, $"The request has no {string.Join(" or ", missing)} header");
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var sent))
        {
            throw new CohoWebhookException(WebhookRefusal.Timestamp, $"webhook-timestamp is not a Unix time: {timestamp}");
        }
        var clock = now.ToUnixTimeSeconds();
        var leeway = (long)tolerance.TotalSeconds;
        if (sent < clock - leeway || sent > clock + leeway)
        {
            throw new CohoWebhookException(WebhookRefusal.Timestamp, $"webhook-timestamp is more than {leeway} seconds from now, so the request may be a replay");
        }

        // Only v1 signatures: a later version, such as v1a, is for a receiver that knows it.
        var candidates = new List<byte[]>();
        foreach (var each in signature.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (each.StartsWith("v1,", StringComparison.Ordinal))
            {
                try
                {
                    candidates.Add(Convert.FromBase64String(each[3..]));
                }
                catch (FormatException)
                {
                    // Not base64, so it matches nothing.
                }
            }
        }

        var prefix = Encoding.UTF8.GetBytes($"{id}.{timestamp}.");
        foreach (var key in keys)
        {
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
            hmac.AppendData(prefix);
            hmac.AppendData(body);
            var expected = hmac.GetHashAndReset();
            foreach (var candidate in candidates)
            {
                if (CryptographicOperations.FixedTimeEquals(expected, candidate))
                {
                    return;
                }
            }
        }
        throw new CohoWebhookException(WebhookRefusal.Signature, "No v1 signature in webhook-signature matches the body with the secret given");
    }

    // The first value, or for webhook-signature every value, as space-separated signatures.
    private static string? Header(IEnumerable<KeyValuePair<string, StringValues>> headers, string name)
    {
        foreach (var (key, values) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                var value = (name == "webhook-signature" ? string.Join(' ', values.ToArray()) : values.FirstOrDefault())?.Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }
        return null;
    }

    private static byte[] Key(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var encoded = secret.StartsWith(SecretPrefix, StringComparison.Ordinal) ? secret[SecretPrefix.Length..] : secret;
        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException e)
        {
            throw new ArgumentException($"A webhook secret is {SecretPrefix} and a key in base64", nameof(secret), e);
        }
        if (key.Length == 0)
        {
            throw new ArgumentException($"A webhook secret is {SecretPrefix} and a key in base64", nameof(secret));
        }
        return key;
    }
}

/// <summary>Why a webhook request was refused: the spec's <c>signature</c>, <c>timestamp</c>, and <c>headers</c>.</summary>
public enum WebhookRefusal
{
    /// <summary>No <c>v1</c> signature matches the body with any secret given.</summary>
    Signature,

    /// <summary><c>webhook-timestamp</c> is too far from the receiver's clock, or not a time.</summary>
    Timestamp,

    /// <summary><c>webhook-id</c>, <c>webhook-timestamp</c>, or <c>webhook-signature</c> is missing.</summary>
    Headers,
}

/// <summary>
/// A webhook request <see cref="Webhooks.Verify(string, IEnumerable{KeyValuePair{string, StringValues}}, string, DateTimeOffset?, TimeSpan?)"/>
/// refused. Answer it with a <c>401</c>: Coho does not retry one, and an owner can redeliver
/// it once the receiver is fixed.
/// </summary>
public sealed class CohoWebhookException : Exception
{
    public CohoWebhookException(WebhookRefusal reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public CohoWebhookException()
        : this(WebhookRefusal.Signature, "The webhook request was refused")
    {
    }

    public CohoWebhookException(string message)
        : this(WebhookRefusal.Signature, message)
    {
    }

    public CohoWebhookException(string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = WebhookRefusal.Signature;
    }

    public WebhookRefusal Reason { get; }
}

/// <summary>One event, as Coho sends it to a webhook.</summary>
public sealed record WebhookEvent
{
    /// <summary>
    /// The event's, the same on every attempt and redelivery, and the <c>webhook-id</c>
    /// header: drop an event already handled.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Such as <c>environment.promoted</c>. Open, so a type added later still reads.</summary>
    public required string Type { get; init; }

    /// <summary>When the event happened.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    public required WebhookProject Project { get; init; }

    /// <summary>Who made the change.</summary>
    public required WebhookActor Actor { get; init; }

    /// <summary>The event's own values: identifiers and counts, never content.</summary>
    public JsonElement Data { get; init; }

    internal static WebhookEvent Parse(ReadOnlySpan<byte> body)
    {
        JsonElement json;
        try
        {
            var reader = new Utf8JsonReader(body);
            json = JsonElement.ParseValue(ref reader);
        }
        catch (JsonException e)
        {
            throw new JsonException("The webhook's body is not JSON", e);
        }

        static string Required(JsonElement value, string name)
        {
            return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()!
                : throw new JsonException($"The webhook's body is not a Coho event: it has no {name}");
        }

        static string? Text(JsonElement value, string name)
        {
            return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        static JsonElement Property(JsonElement value, string name)
        {
            return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;
        }

        if (!DateTimeOffset.TryParse(Required(json, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var createdAt))
        {
            throw new JsonException("The webhook's created_at is not a time");
        }
        var project = Property(json, "project");
        var actor = Property(json, "actor");
        return new WebhookEvent
        {
            Id = Required(json, "id"),
            Type = Required(json, "type"),
            CreatedAt = createdAt,
            Project = new WebhookProject { Id = Required(project, "id"), Name = Text(project, "name") ?? "" },
            Actor = new WebhookActor { Kind = Required(actor, "kind"), Id = Text(actor, "id"), Label = Text(actor, "label") },
            Data = Property(json, "data"),
        };
    }
}

/// <summary>The project an event happened in.</summary>
public sealed record WebhookProject
{
    public required string Id { get; init; }

    public string Name { get; init; } = "";
}

/// <summary>Who made the change.</summary>
public sealed record WebhookActor
{
    /// <summary><c>person</c>, <c>token</c>, or <c>system</c>. Open, so a kind added later still reads.</summary>
    public required string Kind { get; init; }

    /// <summary>The person's or the token's id. Null for <c>system</c>.</summary>
    public string? Id { get; init; }

    /// <summary>A token's label. Null for anyone else.</summary>
    public string? Label { get; init; }
}
