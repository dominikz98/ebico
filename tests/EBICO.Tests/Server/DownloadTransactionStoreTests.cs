using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Tests.Server;

/// <summary>
/// Unit tests for the download-transaction state layer (issue #33): the in-memory transaction store,
/// the <see cref="DownloadTransaction"/> segment access, and the in-memory download-data provider's
/// FIFO queue — independently of the pipeline.
/// </summary>
public class DownloadTransactionStoreTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static SubscriberKeyRef KeyRef(string user = "USER01")
        => new(HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), UserId.Create(user));

    private static DownloadTransaction NewTransaction(byte[] id, params byte[][] segments)
        => new(
            id,
            EbicsVersion.H004,
            KeyRef(),
            "FDL",
            segments,
            encryptedTransactionKey: [1, 2, 3],
            encryptionPubKeyDigest: [4, 5, 6],
            encryptionVersion: KeyVersion.Create("E002"),
            orderDataPlaintext: [7, 8, 9],
            createdAt: DateTimeOffset.UnixEpoch);

    // --- Store -----------------------------------------------------------------------------

    [Fact]
    public void Store_CreateThenTryGet_RoundTripsByHexId()
    {
        var store = new InMemoryDownloadTransactionStore();
        var id = Enumerable.Repeat((byte)0x0A, 16).ToArray();
        var transaction = NewTransaction(id, [1, 2, 3]);

        store.Create(transaction);

        store.Count.Should().Be(1);
        store.TryGet(Convert.ToHexString(id), out var found).Should().BeTrue();
        found.Should().BeSameAs(transaction);
    }

    [Fact]
    public void Store_TryGetUnknownId_ReturnsFalseAndNull()
    {
        var store = new InMemoryDownloadTransactionStore();

        store.TryGet(Convert.ToHexString(new byte[16]), out var found).Should().BeFalse();
        found.Should().BeNull();
    }

    [Fact]
    public void Store_CreateDuplicateId_Throws()
    {
        var store = new InMemoryDownloadTransactionStore();
        var id = Enumerable.Repeat((byte)0x0B, 16).ToArray();
        store.Create(NewTransaction(id, [1]));

        var act = () => store.Create(NewTransaction(id, [2]));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Store_Remove_DeletesAndReportsWhetherPresent()
    {
        var store = new InMemoryDownloadTransactionStore();
        var id = Enumerable.Repeat((byte)0x0C, 16).ToArray();
        var hex = Convert.ToHexString(id);
        store.Create(NewTransaction(id, [1]));

        store.Remove(hex).Should().BeTrue();
        store.Count.Should().Be(0);
        store.Remove(hex).Should().BeFalse();
    }

    // --- DownloadTransaction ---------------------------------------------------------------

    [Fact]
    public void Transaction_GetSegment_IsOneBased()
    {
        var transaction = NewTransaction(new byte[16], [10], [20], [30]);

        transaction.NumSegments.Should().Be(3);
        transaction.GetSegment(1).Should().Equal([10]);
        transaction.GetSegment(3).Should().Equal([30]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Transaction_GetSegment_OutOfRange_Throws(int segmentNumber)
    {
        var transaction = NewTransaction(new byte[16], [10], [20], [30]);

        var act = () => transaction.GetSegment(segmentNumber);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Transaction_WithNoSegments_Throws()
    {
        var act = () => NewTransaction(new byte[16]);

        act.Should().Throw<ArgumentException>();
    }

    // --- InMemoryDownloadDataProvider ------------------------------------------------------

    [Fact]
    public async Task Provider_DequeueWhenEmpty_ReturnsNull()
    {
        var provider = new InMemoryDownloadDataProvider();

        var data = await provider.TryDequeueAsync(new DownloadDataRequest(EbicsVersion.H004, KeyRef(), "FDL"), _ct);

        data.Should().BeNull();
        (await provider.CountAsync(KeyRef(), "FDL", _ct)).Should().Be(0);
    }

    [Fact]
    public async Task Provider_EnqueueThenDequeue_IsFifo()
    {
        var provider = new InMemoryDownloadDataProvider();
        await provider.EnqueueAsync(KeyRef(), "FDL", [1], _ct);
        await provider.EnqueueAsync(KeyRef(), "FDL", [2], _ct);

        (await provider.CountAsync(KeyRef(), "FDL", _ct)).Should().Be(2);
        (await provider.TryDequeueAsync(new DownloadDataRequest(EbicsVersion.H004, KeyRef(), "FDL"), _ct)).Should().Equal([1]);
        (await provider.TryDequeueAsync(new DownloadDataRequest(EbicsVersion.H004, KeyRef(), "FDL"), _ct)).Should().Equal([2]);
        (await provider.CountAsync(KeyRef(), "FDL", _ct)).Should().Be(0);
    }

    [Fact]
    public async Task Provider_KeysAreIsolatedBySubscriberAndOrderType()
    {
        var provider = new InMemoryDownloadDataProvider();
        await provider.EnqueueAsync(KeyRef("USER01"), "FDL", [1], _ct);

        // A different order type and a different subscriber both see an empty queue.
        (await provider.CountAsync(KeyRef("USER01"), "BTD", _ct)).Should().Be(0);
        (await provider.CountAsync(KeyRef("USER02"), "FDL", _ct)).Should().Be(0);
        (await provider.TryDequeueAsync(new DownloadDataRequest(EbicsVersion.H004, KeyRef("USER02"), "FDL"), _ct)).Should().BeNull();

        // The original queue is untouched.
        (await provider.CountAsync(KeyRef("USER01"), "FDL", _ct)).Should().Be(1);
    }
}
