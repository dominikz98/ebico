using EBICO.Server.State;
using EBICO.Server.Transactions;

namespace EBICO.Suite.Services;

/// <summary>Whether a transaction is an upload or a download, as reconstructed from its events.</summary>
public enum TransactionKind
{
    /// <summary>The direction could not be determined from the available events/order type.</summary>
    Unknown,

    /// <summary>An upload transaction (the subscriber sends order data to the bank).</summary>
    Upload,

    /// <summary>A download transaction (the bank serves order data to the subscriber).</summary>
    Download,
}

/// <summary>The lifecycle status of a transaction, derived from its events.</summary>
public enum TransactionStatus
{
    /// <summary>In flight: initialised but not yet completed, failed or evicted.</summary>
    Running,

    /// <summary>Completed successfully (upload reassembled / download positively acknowledged).</summary>
    Completed,

    /// <summary>Ended in a rejection: a business/technical error or a negative download receipt.</summary>
    Failed,

    /// <summary>Expired and evicted by the idle-timeout sweep before completing.</summary>
    Evicted,
}

/// <summary>
/// A one-line summary of a transaction for the inspector list (issue #54), reconstructed from the
/// <see cref="IEventLog"/> and enriched from the transaction stores while the transaction is resident.
/// </summary>
public sealed record TransactionSummary
{
    /// <summary>The upper-case hex transaction id (the key across event log, stores and captures).</summary>
    public required string TransactionIdHex { get; init; }

    /// <summary>Whether this is an upload or a download.</summary>
    public required TransactionKind Kind { get; init; }

    /// <summary>The order type (e.g. <c>"BTU"</c>/<c>"BTD"</c>), or <see langword="null"/> when unknown.</summary>
    public string? OrderType { get; init; }

    /// <summary>The host id label, or <see langword="null"/>.</summary>
    public string? HostLabel { get; init; }

    /// <summary>The customer (Partner) id label, or <see langword="null"/>.</summary>
    public string? PartnerLabel { get; init; }

    /// <summary>The subscriber (User) id label, or <see langword="null"/>.</summary>
    public string? UserLabel { get; init; }

    /// <summary>The announced number of segments, or <see langword="null"/> when the transaction is no longer resident.</summary>
    public int? NumSegments { get; init; }

    /// <summary>The derived lifecycle status.</summary>
    public required TransactionStatus Status { get; init; }

    /// <summary>The last known return code as <c>"code symbolicName"</c>, or <see langword="null"/>.</summary>
    public string? LastReturnCode { get; init; }

    /// <summary>The timestamp of the first event of the transaction.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The timestamp of the last event of the transaction.</summary>
    public required DateTimeOffset LastActivityAt { get; init; }

    /// <summary>The number of events recorded for the transaction.</summary>
    public int EventCount { get; init; }

    /// <summary>Whether raw request/response XML was captured for the transaction.</summary>
    public bool HasCapture { get; init; }

    /// <summary>Whether the transaction is still held in a transaction store (so its order data is available).</summary>
    public bool IsResident { get; init; }
}

/// <summary>The decrypted, decompressed order data of a transaction, prepared for display (issue #54).</summary>
public sealed record OrderDataView
{
    /// <summary>The full byte length of the order data (before any display truncation).</summary>
    public required int ByteLength { get; init; }

    /// <summary>Whether the order data looks like text (so <see cref="Text"/> is meaningful).</summary>
    public required bool IsText { get; init; }

    /// <summary>Whether the preview was truncated (the order data exceeds the display cap).</summary>
    public bool Truncated { get; init; }

    /// <summary>The UTF-8 decoded text preview (empty when the data is binary).</summary>
    public required string Text { get; init; }

    /// <summary>The upper-case hex preview.</summary>
    public required string Hex { get; init; }
}

/// <summary>A captured request/response XML pair of one transaction phase, prepared for display (issue #54).</summary>
public sealed record CapturedMessageView
{
    /// <summary>The transaction phase the captured message was processed in.</summary>
    public required EbicsTransactionPhase Phase { get; init; }

    /// <summary>The segment number of a transfer-phase message, or <see langword="null"/>.</summary>
    public int? SegmentNumber { get; init; }

    /// <summary>When the message was captured.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The raw request XML (possibly truncated — see <see cref="RequestTruncated"/>).</summary>
    public required string RequestXml { get; init; }

    /// <summary>The raw response XML (possibly truncated — see <see cref="ResponseTruncated"/>).</summary>
    public required string ResponseXml { get; init; }

    /// <summary>Whether <see cref="RequestXml"/> was truncated.</summary>
    public bool RequestTruncated { get; init; }

    /// <summary>Whether <see cref="ResponseXml"/> was truncated.</summary>
    public bool ResponseTruncated { get; init; }
}

/// <summary>A single event, prepared for display in the global protocol view (issue #54).</summary>
public sealed record EventView
{
    /// <summary>The monotonic sequence number.</summary>
    public long Sequence { get; init; }

    /// <summary>When the event was appended.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The event type.</summary>
    public required EbicsEventType Type { get; init; }

    /// <summary>The severity.</summary>
    public EbicsEventSeverity Severity { get; init; }

    /// <summary>The visibility (customer-visible vs. operator-internal).</summary>
    public EbicsEventVisibility Visibility { get; init; }

    /// <summary>The host id label, or <see langword="null"/>.</summary>
    public string? HostLabel { get; init; }

    /// <summary>The customer (Partner) id label, or <see langword="null"/>.</summary>
    public string? PartnerLabel { get; init; }

    /// <summary>The subscriber (User) id label, or <see langword="null"/>.</summary>
    public string? UserLabel { get; init; }

    /// <summary>The order type, or <see langword="null"/>.</summary>
    public string? OrderType { get; init; }

    /// <summary>The hex transaction id the event relates to, or <see langword="null"/> for non-transaction events.</summary>
    public string? TransactionIdHex { get; init; }

    /// <summary>The return code as <c>"code symbolicName"</c>, or <see langword="null"/>.</summary>
    public string? ReturnCode { get; init; }

    /// <summary>The human-readable message.</summary>
    public required string Message { get; init; }
}

/// <summary>The full detail of one transaction for the inspector (issue #54).</summary>
public sealed record TransactionDetail
{
    /// <summary>The summary line.</summary>
    public required TransactionSummary Summary { get; init; }

    /// <summary>The events of the transaction, ascending by sequence.</summary>
    public required IReadOnlyList<EventView> Events { get; init; }

    /// <summary>The captured raw request/response XML per phase, ascending by sequence.</summary>
    public required IReadOnlyList<CapturedMessageView> Messages { get; init; }

    /// <summary>The decrypted order data, or <see langword="null"/> when the transaction is not resident (or not yet complete).</summary>
    public OrderDataView? OrderData { get; init; }
}

/// <summary>
/// The live filter for the global protocol view (issue #54). Every field is optional; unset fields do not
/// filter. Customer/type/time are pushed down to <see cref="IEventLog.QueryAsync"/>; severity is applied
/// by the provider because <see cref="EbicsEventQuery"/> carries no severity dimension.
/// </summary>
public sealed record EventLogFilter
{
    /// <summary>The customer (Partner) id to filter on, or <see langword="null"/> for all customers.</summary>
    public string? Partner { get; init; }

    /// <summary>The event type to filter on, or <see langword="null"/> for all types.</summary>
    public EbicsEventType? Type { get; init; }

    /// <summary>The severity to filter on, or <see langword="null"/> for all severities.</summary>
    public EbicsEventSeverity? Severity { get; init; }

    /// <summary>The inclusive lower time bound, or <see langword="null"/>.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>The exclusive upper time bound, or <see langword="null"/>.</summary>
    public DateTimeOffset? To { get; init; }
}
