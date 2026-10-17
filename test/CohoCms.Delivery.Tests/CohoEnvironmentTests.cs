using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CohoCms.Delivery.Tests;

public sealed class CohoEnvironmentTests
{
    private static ContentConfig From(Dictionary<string, string?> env) => CohoEnvironment.From(env.GetValueOrDefault);

    [Fact]
    public void A_delivery_key_reads_released_content_from_delivery()
    {
        var config = From(new()
        {
            ["COHO_DELIVERY_URL"] = "https://delivery.test",
            ["COHO_DELIVERY_KEY"] = "k",
            ["COHO_PROJECT"] = "p1",
            ["COHO_REF"] = "prod",
            ["COHO_LOCALE"] = "en-US",
        });

        Assert.Equal(new ReleasedConfig { Url = "https://delivery.test", Key = "k", Project = "p1" }, config.Delivery);
        Assert.Equal(("prod", "en-US"), (config.Ref, config.Locale));
    }

    [Fact]
    public void A_preview_token_previews_unreleased_content_through_Coho()
    {
        var config = From(new()
        {
            ["COHO_URL"] = "https://coho.test",
            ["COHO_ACCOUNT"] = "acme",
            ["COHO_PROJECT"] = "p1",
            ["COHO_REF"] = "dev",
            ["COHO_PREVIEW_TOKEN"] = Spec.Token,
        });

        Assert.Equal(new PreviewConfig { Url = "https://coho.test", Account = "acme", Project = "p1", Token = Spec.Token }, config.Delivery);
        Assert.Equal(("dev", null), (config.Ref, config.Locale));
    }

    [Fact]
    public void A_public_environment_needs_no_key_and_an_empty_variable_is_unset()
    {
        var config = From(new()
        {
            ["COHO_DELIVERY_URL"] = "https://delivery.test",
            ["COHO_DELIVERY_KEY"] = "",
            ["COHO_PROJECT"] = "p1",
            ["COHO_REF"] = "prod",
            ["COHO_PREVIEW_TOKEN"] = " ",
        });

        Assert.Null(Assert.IsType<ReleasedConfig>(config.Delivery).Key);
    }

    [Fact]
    public void What_is_missing_is_named_for_what_is_being_read()
    {
        var released = Assert.Throws<InvalidOperationException>(() => From(new() { ["COHO_PROJECT"] = "p1" }));
        Assert.Contains("COHO_DELIVERY_URL, COHO_REF are needed to read released content", released.Message, StringComparison.Ordinal);

        var preview = Assert.Throws<InvalidOperationException>(() => From(new() { ["COHO_PREVIEW_TOKEN"] = Spec.Token, ["COHO_PROJECT"] = "p1", ["COHO_REF"] = "dev" }));
        Assert.Contains("COHO_URL, COHO_ACCOUNT are needed to read a preview, since COHO_PREVIEW_TOKEN is set", preview.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_and_a_preview_token_together_are_refused_not_chosen_between()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => From(new()
        {
            ["COHO_URL"] = "u",
            ["COHO_ACCOUNT"] = "a",
            ["COHO_DELIVERY_URL"] = "u",
            ["COHO_PROJECT"] = "p",
            ["COHO_REF"] = "dev",
            ["COHO_DELIVERY_KEY"] = "k",
            ["COHO_PREVIEW_TOKEN"] = Spec.Token,
        }));

        Assert.Contains("not both", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configuration_under_the_same_names_gives_an_apps_content_client()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COHO_URL"] = "https://coho.test",
                ["COHO_ACCOUNT"] = "acme",
                ["COHO_PROJECT"] = "p1",
                ["COHO_REF"] = "dev",
                ["COHO_PREVIEW_TOKEN"] = Spec.Token,
            })
            .Build();
        var handler = Answering.Ok(cacheControl: "no-cache");
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddCohoContent().ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ContentClient>().Type<HydratedEntry>("article").ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["https://coho.test/api/v1/accounts/acme/projects/p1/preview/dev/entries?type=article"], handler.Calls.Select(call => call.Url));
    }
}
