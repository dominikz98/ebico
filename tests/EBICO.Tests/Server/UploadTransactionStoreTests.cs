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

    [Fact]
    public void GetAll_EmptyStore_ReturnsEmpty()
    {
        var store = new InMemoryUploadTransactionStore();

        store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void GetAll_ReturnsAllTransactions()
    {
        var store = new InMemoryUploadTransactionStore();
        var a = NewTransaction(Enumerable.Repeat((byte)1, 16).ToArray());
        var b = NewTransaction(Enumerable.Repeat((byte)2, 16).ToArray());
        store.Create(a);
        store.Create(b);

        store.GetAll().Should().BeEquivalentTo([a, b]);
    }

    [Fact]
    public void GetAll_SnapshotUnaffectedByLaterRemove()
    {
        var store = new InMemoryUploadTransactionStore();
        var transaction = NewTransaction(Enumerable.Repeat((byte)3, 16).ToArray());
        store.Create(transaction);

        var snapshot = store.GetAll();
        store.Remove(transaction.TransactionIdHex);

        snapshot.Should().ContainSingle().Which.Should().BeSameAs(transaction);
    }

    // --- Idle expiry (UploadTransaction) ---------------------------------------------------

    [Fact]
    public void LastActivityAt_Initially_EqualsCreatedAt()
    {
        var transaction = NewTransaction(new byte[16]);

        transaction.LastActivityAt.Should().Be(transaction.CreatedAt);
    }

    [Fact]
    public void IsExpired_BeforeTimeout_ReturnsFalse()
    {
        var transaction = NewTransaction(new byte[16]);
        var now = transaction.CreatedAt + TimeSpan.FromMinutes(59);

        transaction.IsExpired(now, TimeSpan.FromHours(1)).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_AtOrAfterTimeout_ReturnsTrue()
    {
        var transaction = NewTransaction(new byte[16]);

        transaction.IsExpired(transaction.CreatedAt + TimeSpan.FromHours(1), TimeSpan.FromHours(1)).Should().BeTrue();
        transaction.IsExpired(transaction.CreatedAt + TimeSpan.FromHours(2), TimeSpan.FromHours(1)).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_WithNonPositiveTimeout_ReturnsFalse()
    {
        var transaction = NewTransaction(new byte[16]);
        var farFuture = transaction.CreatedAt + TimeSpan.FromDays(365);

        transaction.IsExpired(farFuture, TimeSpan.Zero).Should().BeFalse();
        transaction.IsExpired(farFuture, TimeSpan.FromSeconds(-1)).Should().BeFalse();
    }

    [Fact]
    public void Touch_SlidesTheExpiryWindow()
    {
        var transaction = NewTransaction(new byte[16]);
        var timeout = TimeSpan.FromHours(1);
        var later = transaction.CreatedAt + TimeSpan.FromMinutes(30);

        transaction.Touch(later);

        transaction.LastActivityAt.Should().Be(later);
        // Would have expired at CreatedAt+1h; after the touch it lives until later+1h.
        transaction.IsExpired(transaction.CreatedAt + TimeSpan.FromMinutes(61), timeout).Should().BeFalse();
        transaction.IsExpired(later + timeout, timeout).Should().BeTrue();
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
