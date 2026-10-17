using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CohoCms.Delivery;

/// <summary>Registers Coho's clients with an app's services.</summary>
public static class CohoServiceCollectionExtensions
{
    /// <summary>The name of the <see cref="HttpClient"/> Coho's clients send requests with.</summary>
    public const string HttpClientName = "CohoCms.Delivery";

    /// <summary>
    /// A <see cref="ContentClient"/>, one for the app, configured from the <c>COHO_*</c>
    /// values in configuration unless <paramref name="config"/> says otherwise.
    /// </summary>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, to add handlers to.</returns>
    public static IHttpClientBuilder AddCohoContent(
        this IServiceCollection services,
        IReadOnlyDictionary<string, TypeShape>? shape = null,
        Func<IServiceProvider, ContentConfig>? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(provider => new ContentClient(ConfigOf(provider, config), shape, OptionsOf(provider)));
        return services.AddHttpClient(HttpClientName);
    }

    /// <summary>A <see cref="DeliveryClient"/>, one for the app.</summary>
    /// <returns>The builder of the client's <see cref="HttpClient"/>, to add handlers to.</returns>
    public static IHttpClientBuilder AddCohoDelivery(this IServiceCollection services, Func<IServiceProvider, DeliveryConfig>? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(provider => new DeliveryClient(config?.Invoke(provider) ?? ConfigOf(provider, null).Delivery, OptionsOf(provider)));
        return services.AddHttpClient(HttpClientName);
    }

    private static ContentConfig ConfigOf(IServiceProvider provider, Func<IServiceProvider, ContentConfig>? config)
    {
        return config?.Invoke(provider) ?? CohoEnvironment.FromConfiguration(provider.GetRequiredService<IConfiguration>());
    }

    private static ClientOptions OptionsOf(IServiceProvider provider)
    {
        return new ClientOptions
        {
            HttpClientFactory = provider.GetRequiredService<IHttpClientFactory>(),
            TimeProvider = provider.GetService<TimeProvider>() ?? TimeProvider.System,
            Logger = provider.GetService<ILoggerFactory>()?.CreateLogger(HttpClientName),
        };
    }
}
