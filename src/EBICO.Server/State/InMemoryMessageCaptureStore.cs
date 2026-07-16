using Microsoft.Extensions.Options;

namespace EBICO.Server.State;

/// <summary>
/// Default in-memory <see cref="IMessageCaptureStore"/>. Thread-safe (a single lock guards the capture log
/// and the monotonic sequence counter), backed by a FIFO queue whose enumeration order equals the
/// append/sequence order. Nothing is persisted across process restarts.
/// </summary>
/// <remarks>
/// Memory is bounded on two axes by <see cref="EbicoServerOptions"/>: the number of retained captures
/// (<see cref="EbicoServerOptions.MaxMessageCaptureEntries"/>, a ring buffer across <em>all</em>
/// transactions) and the stored size of each XML document
/// (<see cref="EbicoServerOptions.MaxCapturedMessageBytes"/>, oversized XML is truncated for display, with
/// the corresponding <c>*Truncated</c> flag set). A cap of <c>0</c> disables the respective bound. A
/// persistent store can replace this via <c>TryAddSingleton&lt;IMessageCaptureStore, …&gt;</c> before
/// <c>AddEbicoServer</c> (ADR-0011/ADR-0015).
/// </remarks>
public sealed class InMemoryMessageCaptureStore : IMessageCaptureStore
{
    private readonly object _gate = new();
    private readonly Queue<CapturedMessage> _messages = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _maxEntries;
    private readonly int _maxBytes;
    private long _sequence;

    /// <summary>Initializes the in-memory message capture store.</summary>
    /// <param name="timeProvider">The clock used to stamp <see cref="CapturedMessage.Timestamp"/> on append.</param>
    /// <param name="options">
    /// The server options; <see cref="EbicoServerOptions.MaxMessageCaptureEntries"/> bounds the number of
    /// captures and <see cref="EbicoServerOptions.MaxCapturedMessageBytes"/> the stored size of each XML.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public InMemoryMessageCaptureStore(TimeProvider timeProvider, IOptions<EbicoServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _timeProvider = timeProvider;
        _maxEntries = options.Value.MaxMessageCaptureEntries;
        _maxBytes = options.Value.MaxCapturedMessageBytes;
    }

    /// <inheritdoc />
    public Task AppendAsync(CapturedMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var (requestXml, requestTruncated) = Truncate(message.RequestXml);
        var (responseXml, responseTruncated) = Truncate(message.ResponseXml);

        lock (_gate)
        {
            var stamped = message with
            {
                Sequence = ++_sequence,
                Timestamp = _timeProvider.GetUtcNow(),
                RequestXml = requestXml,
                ResponseXml = responseXml,
                RequestTruncated = requestTruncated,
                ResponseTruncated = responseTruncated,
            };

            _messages.Enqueue(stamped);

            // Ring-buffer eviction: keep at most _maxEntries captures (0 = unbounded).
            while (_maxEntries > 0 && _messages.Count > _maxEntries)
            {
                _messages.Dequeue();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CapturedMessage>> GetAsync(string transactionIdHex, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transactionIdHex);

        var results = new List<CapturedMessage>();
        lock (_gate)
        {
            // The queue enumerates FIFO, i.e. in ascending Sequence order; no explicit sort needed.
            foreach (var message in _messages)
            {
                if (string.Equals(message.TransactionIdHex, transactionIdHex, StringComparison.Ordinal))
                {
                    results.Add(message);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<CapturedMessage>>(results);
    }

    // Bounds a stored XML document to _maxBytes characters (0 = unbounded). EBICS envelopes are
    // UTF-8 and ASCII-dominant, so the character budget tracks the byte budget closely; truncation is a
    // display cap only — the authoritative decrypted order data comes from the transaction store.
    private (string Text, bool Truncated) Truncate(string value)
    {
        if (_maxBytes <= 0 || value.Length <= _maxBytes)
        {
            return (value, false);
        }

        return (value[.._maxBytes], true);
    }
}
