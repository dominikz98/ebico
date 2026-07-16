using AwesomeAssertions;
using EBICO.Server;
using EBICO.Server.State;
using EBICO.Suite.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Suite;

/// <summary>
/// Tests for the <see cref="TransactionInspectorSeeder"/> (issue #54): it fills the event log, transaction
/// stores and capture store with sample data, and is idempotent (a second call is a no-op).
/// </summary>
public class TransactionInspectorSeederTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddOptions<EbicoServerOptions>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEventLog, InMemoryEventLog>();
        services.AddSingleton<IUploadTransactionStore, InMemoryUploadTransactionStore>();
        services.AddSingleton<IDownloadTransactionStore, InMemoryDownloadTransactionStore>();
        services.AddSingleton<IMessageCaptureStore, InMemoryMessageCaptureStore>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SeedAsync_FillsEventLogStoresAndCaptures()
    {
        using var provider = BuildProvider();

        await TransactionInspectorSeeder.SeedAsync(provider, _ct);

        var log = provider.GetRequiredService<IEventLog>();
        var uploads = provider.GetRequiredService<IUploadTransactionStore>();
        var downloads = provider.GetRequiredService<IDownloadTransactionStore>();
        var captures = provider.GetRequiredService<IMessageCaptureStore>();

        (await log.QueryAsync(new EbicsEventQuery(), _ct)).Should().NotBeEmpty();
        uploads.Count.Should().BeGreaterThan(0);
        downloads.Count.Should().BeGreaterThan(0);

        // The completed upload (transaction id 0x11 × 16) has its raw XML captured.
        var completedUploadHex = Convert.ToHexString(Enumerable.Repeat((byte)0x11, 16).ToArray());
        (await captures.GetAsync(completedUploadHex, _ct)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var provider = BuildProvider();
        var uploads = provider.GetRequiredService<IUploadTransactionStore>();

        await TransactionInspectorSeeder.SeedAsync(provider, _ct);
        var countAfterFirst = uploads.Count;
        await TransactionInspectorSeeder.SeedAsync(provider, _ct);

        uploads.Count.Should().Be(countAfterFirst);
    }
}
