using EBICO.Connector;
using EBICO.Connector.Configuration;
using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for the EBICO connector.
/// </summary>
public static class EbicoConnectorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EBICO connector: connection options and their validator, the resolved
    /// <see cref="EbicsConnection"/>, the default in-memory key store, the HTTP transport, the
    /// <see cref="IEbicsClient"/>, and a named <c>HttpClient</c>
    /// (<see cref="EbicoConnector.HttpClientName"/>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the connection parameters (URL, HostID, PartnerID, UserID, version).</param>
    /// <returns>
    /// The <see cref="IHttpClientBuilder"/> for the connector's named client, so callers can chain
    /// timeout and resilience configuration (e.g. <c>.AddStandardResilienceHandler()</c>).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IHttpClientBuilder AddEbicoConnector(
        this IServiceCollection services,
        Action<EbicsConnectionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<EbicsConnectionOptions>().Configure(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<EbicsConnectionOptions>, EbicsConnectionOptionsValidator>());

        services.TryAddSingleton(static sp =>
            EbicsConnection.FromOptions(sp.GetRequiredService<IOptions<EbicsConnectionOptions>>().Value));
        services.TryAddSingleton<IKeyStore, InMemoryKeyStore>();
        services.TryAddTransient<ITransport, HttpClientTransport>();
        services.TryAddSingleton<IEbicsClient, EbicsClient>();

        return services.AddHttpClient(EbicoConnector.HttpClientName, static (sp, http) =>
        {
            http.BaseAddress = sp.GetRequiredService<EbicsConnection>().Url;
        });
    }
}
