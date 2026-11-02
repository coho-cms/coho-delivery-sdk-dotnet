using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Primitives;

namespace CohoCms.Delivery.Tests;

/// <summary>
/// The spec's webhook signatures, made by the sender's own code (coho-data doc 70 §5): what a
/// receiver accepts and refuses.
/// </summary>
public sealed class WebhookTests
{
    private static readonly JsonNode Signatures =
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "spec", "webhooks", "signatures.json")))!;

    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds((long)Signatures["toleranceSeconds"]!);

    private static IEnumerable<JsonNode> Cases => Signatures["cases"]!.AsArray().Select(each => each!);

    private static JsonNode Named(string name) => Cases.Single(each => (string)each["name"]! == name);

    private static Dictionary<string, StringValues> Headers(JsonNode @case) =>
        @case["headers"]!.AsObject().ToDictionary(header => header.Key, header => new StringValues((string)header.Value!));

    private static string Body(JsonNode @case) => (string)@case["body"]!;

    private static string Secret(JsonNode @case) => (string)@case["secret"]!;

    private static DateTimeOffset Now(JsonNode @case) => DateTimeOffset.FromUnixTimeSeconds((long)@case["now"]!);

    public static TheoryData<string> CaseNames => new(Cases.Select(each => (string)each["name"]!));

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Every_case_in_the_spec_is_accepted_or_refused_as_the_sender_says(string name)
    {
        var @case = Named(name);
        var body = Encoding.UTF8.GetBytes(Body(@case));
        if ((bool)@case["valid"]!)
        {
            Webhooks.Check(body, Headers(@case), [Secret(@case)], Now(@case), Tolerance);
        }
        else
        {
            var refused = Assert.Throws<CohoWebhookException>(() => Webhooks.Check(body, Headers(@case), [Secret(@case)], Now(@case), Tolerance));
            Assert.Equal((string)@case["reason"]!, refused.Reason.ToString().ToLowerInvariant());
        }
    }

    [Fact]
    public void The_spec_s_tolerance_is_the_default()
    {
        Assert.Equal(Tolerance, Webhooks.DefaultTolerance);
    }

    [Fact]
    public void A_verified_Coho_event_is_read_typed()
    {
        var @case = Named("coho-event");

        var hook = Webhooks.Verify(Body(@case), Headers(@case), Secret(@case), Now(@case));

        Assert.Equal("evt_01a19cf4b6c07fc4947ff43db9a8961a", hook.Id);
        Assert.Equal((string)@case["headers"]!["webhook-id"]!, hook.Id);
        Assert.Equal("environment.promoted", hook.Type);
        Assert.Equal(DateTimeOffset.Parse("2026-11-02T14:03:11.204Z", System.Globalization.CultureInfo.InvariantCulture), hook.CreatedAt);
        Assert.Equal(new WebhookProject { Id = "4f6c0b9e-2a51-4d7e-9f0a-6b1c2d3e4f50", Name = "Café Zürich — site" }, hook.Project);
        Assert.Equal(new WebhookActor { Kind = "person", Id = "9b21c7e4-5d3f-4a2b-8c1d-0e9f8a7b6c5d" }, hook.Actor);
        Assert.Equal("v1.4.0", hook.Data.GetProperty("target").GetProperty("name").GetString());
    }

    [Fact]
    public void Bytes_and_text_verify_alike_and_the_body_s_UTF_8_is_what_is_signed()
    {
        var @case = Named("coho-event");
        var bytes = Encoding.UTF8.GetBytes(Body(@case));

        Assert.Equal("Café Zürich — site", Webhooks.Verify(bytes, Headers(@case), Secret(@case), Now(@case)).Project.Name);
        var latin1 = Encoding.Latin1.GetString(bytes);
        var refused = Assert.Throws<CohoWebhookException>(() => Webhooks.Verify(latin1, Headers(@case), Secret(@case), Now(@case)));
        Assert.Equal(WebhookRefusal.Signature, refused.Reason);
    }

    [Fact]
    public void Any_one_of_several_secrets_verifies()
    {
        var @case = Named("rotation-current-secret");
        var current = Secret(@case);
        var previous = Secret(Named("rotation-previous-secret"));
        var other = Secret(Named("wrong-secret"));

        string[][] orders = [[current, previous], [previous, current], [other, previous], [other, current]];
        foreach (var secrets in orders)
        {
            Assert.Equal("environment.promoted", Webhooks.Verify(Body(@case), Headers(@case), secrets, Now(@case)).Type);
        }
        // A single signature, from the current secret, against the previous one and another.
        var single = Named("coho-event");
        var refused = Assert.Throws<CohoWebhookException>(() => Webhooks.Verify(Body(single), Headers(single), [previous, other], Now(single)));
        Assert.Equal(WebhookRefusal.Signature, refused.Reason);
    }

    [Fact]
    public void Header_names_are_matched_ignoring_case()
    {
        var @case = Named("coho-event");
        Func<string, string>[] spellings =
        [
            name => name.ToUpperInvariant(),
            name => string.Join('-', name.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..])),
        ];

        foreach (var spelling in spellings)
        {
            var spelled = Headers(@case).ToDictionary(header => spelling(header.Key), header => header.Value);
            Assert.Equal("evt_01a19cf4b6c07fc4947ff43db9a8961a", Webhooks.Verify(Body(@case), spelled, Secret(@case), Now(@case)).Id);
        }
    }

    [Fact]
    public void Signatures_split_across_header_values_are_all_tried()
    {
        var @case = Named("rotation-previous-secret");
        var headers = Headers(@case);
        headers["webhook-signature"] = new StringValues(((string?)headers["webhook-signature"])!.Split(' '));

        Assert.Equal("environment.promoted", Webhooks.Verify(Body(@case), headers, Secret(@case), Now(@case)).Type);
    }

    [Fact]
    public void The_clock_defaults_to_now_so_a_recorded_request_has_long_expired()
    {
        var @case = Named("coho-event");
        var refused = Assert.Throws<CohoWebhookException>(() => Webhooks.Verify(Body(@case), Headers(@case), Secret(@case)));
        Assert.Equal(WebhookRefusal.Timestamp, refused.Reason);
    }

    [Fact]
    public void A_tolerance_can_be_narrowed()
    {
        var @case = Named("tolerance-edge-past");
        var refused = Assert.Throws<CohoWebhookException>(() => Webhooks.Verify(Body(@case), Headers(@case), Secret(@case), Now(@case), TimeSpan.FromMinutes(1)));
        Assert.Equal(WebhookRefusal.Timestamp, refused.Reason);
    }

    [Fact]
    public void A_timestamp_that_is_not_a_number_and_an_empty_header_are_refused()
    {
        var @case = Named("coho-event");

        var notANumber = Headers(@case);
        notANumber["webhook-timestamp"] = "yesterday";
        Assert.Equal(WebhookRefusal.Timestamp, Assert.Throws<CohoWebhookException>(() => Webhooks.Verify(Body(@case), notANumber, Secret(@case), Now(@case))).Reason);
        var empty = Headers(@case);
        empty["webhook-id"] = " ";
        Assert.Equal(WebhookRefusal.Headers, Assert.Throws<CohoWebhookException>(() => Webhooks.Verify(Body(@case), empty, Secret(@case), Now(@case))).Reason);
    }

    [Fact]
    public void A_secret_that_is_not_whsec_and_base64_is_the_caller_s_mistake_not_a_refusal()
    {
        var @case = Named("coho-event");
        Assert.Throws<ArgumentException>(() => Webhooks.Verify(Body(@case), Headers(@case), "whsec_not base64!", Now(@case)));
        Assert.Throws<ArgumentException>(() => Webhooks.Verify(Body(@case), Headers(@case), Array.Empty<string>(), Now(@case)));
    }

    [Fact]
    public void A_body_that_verifies_but_is_not_a_Coho_event_is_not_read_as_one()
    {
        var @case = Named("standard-webhooks-example");
        Assert.Throws<JsonException>(() => Webhooks.Verify(Body(@case), Headers(@case), Secret(@case), Now(@case)));
    }

    [Fact]
    public void A_system_actor_has_no_id()
    {
        var hook = WebhookEvent.Parse("""
            {"id":"evt_1","type":"project.updated","created_at":"2026-11-02T14:03:11Z","project":{"id":"p1","name":"Site"},"actor":{"kind":"system"},"data":{}}
            """u8);

        Assert.Equal("system", hook.Actor.Kind);
        Assert.Null(hook.Actor.Id);
        Assert.Null(hook.Actor.Label);
    }
}
