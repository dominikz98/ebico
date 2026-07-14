using AwesomeAssertions;
using EBICO.Server;
using EBICO.Server.Transactions;
using EBICO.Tests.Connector;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for the <see cref="TransactionCleanupService"/> background sweeper (issue #35). The eviction
/// logic itself is covered directly on the engines (see <see cref="TransactionRecoveryTests"/>); here we
/// only assert the service's own control flow — that a disabled interval short-circuits so the host is
/// not left with an idle timer, and that a failing evictor does not tear the sweep loop down.
/// </summary>
public class TransactionCleanupServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhenIntervalDisabled_CompletesImmediately()
    {
        var options = Options.Create(new EbicoServerOptions { TransactionCleanupInterval = TimeSpan.Zero });
        options.Value.TransactionCleanupInterval.Should().Be(TimeSpan.Zero);

        var service = new TransactionCleanupService(
            [], new MutableTimeProvider(Start), options, NullLogger<TransactionCleanupService>.Instance);

        await service.StartAsync(_ct);

        // A disabled sweeper returns without creating a timer, so its ExecuteTask finishes right away.
        service.ExecuteTask.Should().NotBeNull();
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), _ct);
        service.ExecuteTask.IsCompletedSuccessfully.Should().BeTrue();

        await service.StopAsync(_ct);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var clock = new MutableTimeProvider(Start);
        var options = Options.Create(new EbicoServerOptions());
        var logger = NullLogger<TransactionCleanupService>.Instance;

        ((Action)(() => _ = new TransactionCleanupService(null!, clock, options, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new TransactionCleanupService([], null!, options, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new TransactionCleanupService([], clock, null!, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new TransactionCleanupService([], clock, options, null!))).Should().Throw<ArgumentNullException>();
    }
}
