using EBICO.Core;

namespace EBICO.Server;

/// <summary>
/// Configuration for the hostable EBICS server (emulator).
/// </summary>
public sealed class EbicoServerOptions
{
    /// <summary>The HTTP path the EBICS endpoint is mapped to. Defaults to <c>/ebics</c>.</summary>
    public string EndpointPath { get; set; } = "/ebics";

    /// <summary>
    /// The route prefix the master-data admin API is mounted at. Defaults to <c>/admin</c>. The
    /// admin API is unauthenticated and intended for local/emulator use only.
    /// </summary>
    public string AdminApiPath { get; set; } = "/admin";

    /// <summary>
    /// The version used to produce an error response when the request version cannot be detected
    /// (e.g. malformed XML). Defaults to <see cref="EbicsVersion.H005"/>.
    /// </summary>
    public EbicsVersion FallbackResponseVersion { get; set; } = EbicsVersion.H005;

    /// <summary>The maximum accepted request body size in bytes. Defaults to 1 MiB.</summary>
    public long MaxRequestBodyBytes { get; set; } = 1 * 1024 * 1024;

    /// <summary>
    /// The maximum raw (pre-base64) size in bytes of a single order-data segment. Defaults to 512 KiB.
    /// The base64 wire size is roughly 4/3 of this (512 KiB &#8594; ~683 KiB), leaving headroom for the
    /// envelope under <see cref="MaxRequestBodyBytes"/> (1 MiB); the hard ceiling that keeps a segment's
    /// base64 form at or below 1 MiB is 768 KiB raw. Consumed by <c>EbicsSegmentation.Split</c> once the
    /// transaction engine (M4 upload/download, issues #32/#33) wires it in.
    /// </summary>
    public int SegmentSizeBytes { get; set; } = 512 * 1024;

    /// <summary>
    /// The maximum number of segments a single upload transaction may announce (<c>NumSegments</c>).
    /// Defaults to 1000. An initialisation that announces more is rejected with
    /// <c>EBICS_MAX_SEGMENTS_EXCEEDED</c> (091114). Consumed by the transaction engine (issue #32).
    /// </summary>
    public int MaxUploadSegments { get; set; } = 1000;

    /// <summary>
    /// The maximum number of segments a single download transaction may produce when the server
    /// segments the order data. Defaults to 1000. Order data that would split into more segments is
    /// rejected in the initialisation phase with <c>EBICS_MAX_SEGMENTS_EXCEEDED</c> (091114). Consumed
    /// by the download transaction engine (issue #33).
    /// </summary>
    public int MaxDownloadSegments { get; set; } = 1000;

    /// <summary>
    /// How long an in-flight upload/download transaction may stay idle before it expires. Defaults to
    /// one hour. The window slides on activity (each accepted transfer step): a transaction expires
    /// <see cref="TransactionTimeout"/> after its <em>last</em> activity, not its creation. An expired
    /// transaction is rejected with <c>EBICS_TX_UNKNOWN_TXID</c> (091101) on the next request against it
    /// (lazy expiry) and swept from the store by the background cleanup service; an expired download
    /// re-enqueues its (already dequeued) order data so it is not lost. This is also the retention window
    /// for idempotency/replay detection — a completed transaction stays recognisable until it expires.
    /// A value of <see cref="TimeSpan.Zero"/> or less disables expiry (transactions live until the
    /// process restarts). Consumed by the transaction engines and the cleanup service (issue #35).
    /// </summary>
    public TimeSpan TransactionTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How often the background cleanup service sweeps the transaction stores for expired transactions.
    /// Defaults to one minute. A value of <see cref="TimeSpan.Zero"/> or less disables the background
    /// sweeper (expiry then only happens lazily, on the next request against a given transaction id).
    /// Consumed by the transaction cleanup service (issue #35).
    /// </summary>
    public TimeSpan TransactionCleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The maximum number of transactions a single transaction store may hold concurrently. Defaults to
    /// <c>0</c>, which means unlimited. When set to a positive value, an initialisation that would exceed
    /// the ceiling is rejected with <c>EBICS_MAX_TRANSACTIONS_EXCEEDED</c> (091115). The check is a soft
    /// limit (the count-then-create is not atomic) and counts completed-but-not-yet-evicted transactions
    /// within the retention window. Consumed by the transaction engines (issue #35).
    /// </summary>
    public int MaxConcurrentTransactions { get; set; }

    /// <summary>
    /// The content types accepted on the EBICS endpoint. Defaults to <c>text/xml</c> and
    /// <c>application/xml</c>.
    /// </summary>
    public IReadOnlyCollection<string> AllowedContentTypes { get; set; } = ["text/xml", "application/xml"];
}
