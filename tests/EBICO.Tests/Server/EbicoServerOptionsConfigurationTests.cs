using AwesomeAssertions;
using EBICO.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for binding <see cref="EbicoServerOptions"/> from configuration (issue #61): the container
/// configures the emulator via the <c>Ebico</c> section (env vars <c>Ebico__EndpointPath</c> …). The
/// binding lives in <c>AddEbicoServer</c> and is null-safe so a bare <see cref="ServiceCollection"/>
/// without an <see cref="IConfiguration"/> keeps working.
/// </summary>
public class EbicoServerOptionsConfigurationTests
{
    private static IConfiguration ConfigWith(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    [Fact]
    public void AddEbicoServer_BindsOptionsFromTheEbicoConfigurationSection()
    {
        var services = new ServiceCollection();
        services.AddSingleton(ConfigWith(
            ("Ebico:EndpointPath", "/custom-ebics"),
            ("Ebico:MaxRequestBodyBytes", "2048")));

        services.AddEbicoServer();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EbicoServerOptions>>().Value;

        options.EndpointPath.Should().Be("/custom-ebics");
        options.MaxRequestBodyBytes.Should().Be(2048);
    }

    [Fact]
    public void AddEbicoServer_CodeDelegateWinsOverConfiguration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(ConfigWith(("Ebico:EndpointPath", "/from-config")));

        services.AddEbicoServer(o => o.EndpointPath = "/from-code");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EbicoServerOptions>>().Value;

        options.EndpointPath.Should().Be("/from-code", "an explicit configure delegate overrides configuration");
    }

    [Fact]
    public void AddEbicoServer_WithoutConfiguration_KeepsDefaults()
    {
        // No IConfiguration registered: the null-safe binding must not throw and defaults must stand.
        var services = new ServiceCollection();

        services.AddEbicoServer();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EbicoServerOptions>>().Value;

        options.EndpointPath.Should().Be("/ebics");
        options.AdminApiPath.Should().Be("/admin");
        options.MaxRequestBodyBytes.Should().Be(1 * 1024 * 1024);
    }
}
