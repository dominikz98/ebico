using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Tests.Server;

/// <summary>
/// Unit tests for the in-memory upload transaction store and the segment-buffering logic of
/// <see cref="UploadTransaction"/> (issue #32).
/// </summary>
public class UploadTransactionStoreTests
{
    private static UploadTransaction NewTransaction(byte[] transactionId, int numSegments = 1)
        => new(
            transactionId,
            EbicsVersion.H004,
            new SubscriberKeyRef(HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), UserId.Create("USER01")),
            "FUL",
            numSegments,
            new byte[16],
            signatureData: null,
            DateTimeOffset.UnixEpoch);

    // --- Store ------------------------------------------------------------------------------

    [Fact]
    public void Create_ThenTryGetByHex_ReturnsSameTransaction()
    {
        var store = new InMemoryUploadTransactionStore();
        var transaction = NewTransaction([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);

        store.Create(transaction);

        store.Count.Should().Be(1);
        store.TryGet(transaction.TransactionIdHex, out var found).Should().BeTrue();
        found.Should().BeSameAs(transaction);
    }

    [Fact]
    public void TryGet_ForUnknownId_ReturnsFalseAndNull()
    {
        var store = new InMemoryUploadTransactionStore();

        store.TryGet("00112233445566778899AABBCCDDEEFF", out var found).Should().BeFalse();
        found.Should().BeNull();
    }

    [Fact]
    public void Remove_RemovesTheTransaction()
    {
        var store = new InMemoryUploadTransactionStore();
        var transaction = NewTransaction(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray());
        store.Create(transaction);

        store.Remove(transaction.TransactionIdHex).Should().BeTrue();
        store.Remove(transaction.TransactionIdHex).Should().BeFalse();
        store.Count.Should().Be(0);
    }

    [Fact]
    public void Create_WithDuplicateId_Throws()
    {
        var store = new InMemoryUploadTransactionStore();
        var id = Enumerable.Repeat((byte)7, 16).ToArray();
        store.Create(NewTransaction(id));

        var act = () => store.Create(NewTransaction(id));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Create_WithNull_Throws()
    {
        var store = new InMemoryUploadTransactionStore();
        var act = () => store.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // --- Segment buffering (UploadTransaction) ---------------------------------------------

    [Fact]
    public void AppendSegment_SingleLastSegment_IsReady()
    {
        var transaction = NewTransaction([.. Enumerable.Range(0, 16).Select(i => (byte)i)], numSegments: 1);

        var result = transaction.AppendSegment(1, [0xAA], lastSegment: true);

        result.Status.Should().Be(SegmentAppendStatus.Ready);
        result.OrderedSegments.Should().ContainSingle().Which.Should().Equal([0xAA]);
    }

    [Fact]
    public void AppendSegment_OutOfOrder_ReassemblesInSegmentOrder()
    {
        var transaction = NewTransaction([.. Enumerable.Range(0, 16).Select(i => (byte)i)], numSegments: 3);

        transaction.AppendSegment(3, [3], lastSegment: false).Status.Should().Be(SegmentAppendStatus.Buffered);
        transaction.AppendSegment(1, [1], lastSegment: false).Status.Should().Be(SegmentAppendStatus.Buffered);
        var ready = transaction.AppendSegment(2, [2], lastSegment: true);

        ready.Status.Should().Be(SegmentAppendStatus.Ready);
        ready.OrderedSegments!.SelectMany(s => s).Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void AppendSegment_DuplicateSegmentNumber_IsDuplicate()
    {
        var transaction = NewTransaction([.. Enumerable.Range(0, 16).Select(i => (byte)i)], numSegments: 3);

        transaction.AppendSegment(1, [1], lastSegment: false).Status.Should().Be(SegmentAppendStatus.Buffered);
        transaction.AppendSegment(1, [1], lastSegment: false).Status.Should().Be(SegmentAppendStatus.Duplicate);
    }

    [Fact]
    public void AppendSegment_LastFlagBeforeAllArrived_IsUnderrun()
    {
        var transaction = NewTransaction([.. Enumerable.Range(0, 16).Select(i => (byte)i)], numSegments: 3);

        transaction.AppendSegment(1, [1], lastSegment: true).Status.Should().Be(SegmentAppendStatus.Underrun);
    }
}
