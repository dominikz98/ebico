using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;

namespace EBICO.Server.State;

/// <summary>
/// A single, immutable entry in the server-side append-only event/protocol log (issue #69). One event
/// records something relevant that happened while processing EBICS traffic — a request being answered,
/// a transaction lifecycle step, a key-management action — with enough structure to feed the two
/// projections that read the log without producing their own: the customer-facing HAC protocol (M5,
/// filtered per customer and to <see cref="EbicsEventVisibility.CustomerVisible"/> events) and the
/// operator-facing Suite inspector (M7, raw and global).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Sequence"/> and <see cref="Timestamp"/> are assigned by the <see cref="IEventLog"/> on
/// append — a writer constructs the event with only its semantic content and the log stamps the
/// ordering and the clock. The subscriber coordinates (<see cref="HostId"/>/<see cref="PartnerId"/>/
/// <see cref="UserId"/>) are nullable because not every event can be attributed to a fully identified
/// subscriber (e.g. a malformed request, or a transfer-phase step whose header carries only the host).
/// </para>
/// <para>
/// The model is intentionally source-agnostic: the same record shape is written from the request
/// pipeline and from the transaction engines. It carries a full <see cref="EbicsReturnCode"/> (code +
/// symbolic name + kind) as its result rather than only a string, so the HAC mapping can key off the
/// symbolic name.
/// </para>
/// </remarks>
public sealed record EbicsEvent
{
    /// <summary>
    /// The monotonic sequence number assigned by the <see cref="IEventLog"/> on append (1-based). It
    /// gives a stable total order even for events sharing a <see cref="Timestamp"/>. A writer leaves
    /// this at its default; the log overwrites it.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// The instant the event was appended, stamped by the <see cref="IEventLog"/> from its
    /// <see cref="TimeProvider"/>. A writer leaves this at its default; the log overwrites it.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>What kind of event this is.</summary>
    public required EbicsEventType Type { get; init; }

    /// <summary>The severity of the event. Defaults to <see cref="EbicsEventSeverity.Info"/>.</summary>
    public EbicsEventSeverity Severity { get; init; } = EbicsEventSeverity.Info;

    /// <summary>
    /// Whether the event is visible to the customer (surfaced by HAC) or internal to the operator only
    /// (Suite inspector). Defaults to <see cref="EbicsEventVisibility.CustomerVisible"/>.
    /// </summary>
    public EbicsEventVisibility Visibility { get; init; } = EbicsEventVisibility.CustomerVisible;

    /// <summary>The bank/host the event relates to, or <see langword="null"/> when not identifiable.</summary>
    public HostId? HostId { get; init; }

    /// <summary>The customer (Partner) the event relates to, or <see langword="null"/> when not identifiable.</summary>
    public PartnerId? PartnerId { get; init; }

    /// <summary>The subscriber (User) the event relates to, or <see langword="null"/> when not identifiable.</summary>
    public UserId? UserId { get; init; }

    /// <summary>The order type the event relates to (e.g. <c>"HPB"</c>, <c>"BTU"</c>), or <see langword="null"/>.</summary>
    public string? OrderType { get; init; }

    /// <summary>The hex transaction id the event relates to, or <see langword="null"/> for non-transaction events.</summary>
    public string? TransactionId { get; init; }

    /// <summary>The EBICS return code that was the outcome of the event, or <see langword="null"/> when not applicable.</summary>
    public EbicsReturnCode? ReturnCode { get; init; }

    /// <summary>A short human-readable description of the event.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// The kind of an <see cref="EbicsEvent"/>. This is the focused starting vocabulary wired in issue #69
/// (a central per-request event plus the transaction-engine lifecycle); it is expected to grow as more
/// write points are added.
/// </summary>
public enum EbicsEventType
{
    /// <summary>An inbound EBICS request was processed and answered (central pipeline write point).</summary>
    RequestReceived,

    /// <summary>An upload transaction was initialised (a transaction id was assigned).</summary>
    UploadStarted,

    /// <summary>An upload transaction received its last segment and the order data was reassembled.</summary>
    UploadCompleted,

    /// <summary>A download transaction was initialised (data provisioned, first segment sent).</summary>
    DownloadStarted,

    /// <summary>A download transaction was acknowledged with a positive receipt (post-processing done).</summary>
    DownloadCompleted,

    /// <summary>A download transaction was acknowledged with a negative receipt (data re-enqueued).</summary>
    ReceiptNegative,

    /// <summary>An in-flight transaction expired and was evicted (idle-timeout / cleanup sweep).</summary>
    TransactionEvicted,
}

/// <summary>The severity of an <see cref="EbicsEvent"/>.</summary>
public enum EbicsEventSeverity
{
    /// <summary>An ordinary, successful event.</summary>
    Info,

    /// <summary>An event that reports a rejection or a non-fatal anomaly (e.g. a business return code).</summary>
    Warning,

    /// <summary>An event that reports an internal/technical failure.</summary>
    Error,
}

/// <summary>Who an <see cref="EbicsEvent"/> is meant for.</summary>
public enum EbicsEventVisibility
{
    /// <summary>Visible to the customer — surfaced by the HAC customer protocol (M5).</summary>
    CustomerVisible,

    /// <summary>Internal to the operator — only shown in the Suite inspector (M7), never by HAC.</summary>
    Internal,
}
