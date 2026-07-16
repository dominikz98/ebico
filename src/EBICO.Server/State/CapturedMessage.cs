using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Server.Transactions;

namespace EBICO.Server.State;

/// <summary>
/// A single, immutable raw-message capture from the request pipeline (issue #54): the request and
/// response XML of one processed EBICS message that belongs to an upload/download transaction, kept so
/// the Suite transaction inspector can show the raw wire XML per transaction phase.
/// </summary>
/// <remarks>
/// <para>
/// One capture records a whole request/response <em>pair</em> — the pipeline produces both in the same
/// pass, so pairing them (rather than storing two directed records) halves the entries and lines the
/// inspector's phase tabs up directly. <see cref="Sequence"/> and <see cref="Timestamp"/> are assigned by
/// the <see cref="IMessageCaptureStore"/> on append (a writer leaves them at their default); the store
/// overwrites them, mirroring <see cref="EbicsEvent"/>.
/// </para>
/// <para>
/// Captures are transaction-scoped: they are keyed by <see cref="TransactionIdHex"/>, so key-management
/// orders (INI/HIA/HPB/…) that carry no transaction id are deliberately not captured (they still appear in
/// the <see cref="IEventLog"/>, only without raw XML). The XML is stored as text (not <c>byte[]</c>) and
/// may be truncated for display — the authoritative decrypted order data comes from the transaction store,
/// not from a possibly-truncated capture.
/// </para>
/// </remarks>
public sealed record CapturedMessage
{
    /// <summary>
    /// The monotonic sequence number assigned by the <see cref="IMessageCaptureStore"/> on append (1-based).
    /// A writer leaves this at its default; the store overwrites it.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// The instant the message was captured, stamped by the <see cref="IMessageCaptureStore"/> from its
    /// <see cref="TimeProvider"/>. A writer leaves this at its default; the store overwrites it.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The upper-case hex form of the transaction id this capture belongs to (the store key).</summary>
    public required string TransactionIdHex { get; init; }

    /// <summary>The transaction phase the captured message was processed in.</summary>
    public required EbicsTransactionPhase Phase { get; init; }

    /// <summary>The acknowledged/delivered segment number of a transfer-phase message, or <see langword="null"/>.</summary>
    public int? SegmentNumber { get; init; }

    /// <summary>The order type the transaction relates to (e.g. <c>"BTU"</c>), or <see langword="null"/>.</summary>
    public string? OrderType { get; init; }

    /// <summary>The bank/host the message relates to, or <see langword="null"/> when not identifiable.</summary>
    public HostId? HostId { get; init; }

    /// <summary>The customer (Partner) the message relates to, or <see langword="null"/> when not identifiable.</summary>
    public PartnerId? PartnerId { get; init; }

    /// <summary>The subscriber (User) the message relates to, or <see langword="null"/> when not identifiable.</summary>
    public UserId? UserId { get; init; }

    /// <summary>The raw request XML as received (possibly truncated — see <see cref="RequestTruncated"/>).</summary>
    public required string RequestXml { get; init; }

    /// <summary>The raw response XML as sent (possibly truncated — see <see cref="ResponseTruncated"/>).</summary>
    public required string ResponseXml { get; init; }

    /// <summary>The EBICS return code that was the outcome of the message, or <see langword="null"/> when not applicable.</summary>
    public EbicsReturnCode? ReturnCode { get; init; }

    /// <summary>Whether <see cref="RequestXml"/> was truncated for storage.</summary>
    public bool RequestTruncated { get; init; }

    /// <summary>Whether <see cref="ResponseXml"/> was truncated for storage.</summary>
    public bool ResponseTruncated { get; init; }
}
