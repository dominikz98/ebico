using System.Text;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Suite.Services;

/// <summary>
/// Live <see cref="ITransactionInspectorProvider"/> bridge over the in-process server state: the
/// <see cref="IEventLog"/> (the authoritative source, since completed transactions leave the transaction
/// stores), enriched from the <see cref="IUploadTransactionStore"/>/<see cref="IDownloadTransactionStore"/>
/// while a transaction is still resident, and joined with the <see cref="IMessageCaptureStore"/> for the
/// raw request/response XML. Realises the ADR-0009 in-process access decision.
/// </summary>
public sealed class TransactionInspectorProvider : ITransactionInspectorProvider
{
    // The order data preview is capped so a large document does not bloat the render; ByteLength always
    // reports the true size and Truncated flags the cut.
    private const int MaxOrderDataPreviewBytes = 64 * 1024;

    private static readonly HashSet<string> UploadOrderTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FUL", "BTU", "CCT", "CDD", "CDB", "CIP", "HVE", "HVS",
    };

    private static readonly HashSet<string> DownloadOrderTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FDL", "BTD", "STA", "VMK", "C53", "C52", "C54", "HAC", "PTK", "HTD", "HKD", "HAA", "HPD",
        "HVU", "HVZ", "HVD", "HVT",
    };

    private readonly IEventLog _eventLog;
    private readonly IUploadTransactionStore _uploads;
    private readonly IDownloadTransactionStore _downloads;
    private readonly IMessageCaptureStore _captures;

    /// <summary>Creates the bridge over the given server stores.</summary>
    /// <param name="eventLog">The append-only event log.</param>
    /// <param name="uploads">The upload transaction store.</param>
    /// <param name="downloads">The download transaction store.</param>
    /// <param name="captures">The raw-message capture store.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public TransactionInspectorProvider(
        IEventLog eventLog,
        IUploadTransactionStore uploads,
        IDownloadTransactionStore downloads,
        IMessageCaptureStore captures)
    {
        ArgumentNullException.ThrowIfNull(eventLog);
        ArgumentNullException.ThrowIfNull(uploads);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(captures);

        _eventLog = eventLog;
        _uploads = uploads;
        _downloads = downloads;
        _captures = captures;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransactionSummary>> GetTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var events = await _eventLog.QueryAsync(new EbicsEventQuery(), cancellationToken).ConfigureAwait(false);

        var summaries = new List<TransactionSummary>();
        foreach (var group in GroupByTransaction(events))
        {
            summaries.Add(await BuildSummaryAsync(group.Key, group.Value, cancellationToken).ConfigureAwait(false));
        }

        // Newest activity first for the operator's list.
        return summaries.OrderByDescending(s => s.LastActivityAt).ThenByDescending(s => s.TransactionIdHex).ToList();
    }

    /// <inheritdoc />
    public async Task<TransactionDetail?> GetTransactionAsync(string transactionIdHex, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionIdHex);

        var all = await _eventLog.QueryAsync(new EbicsEventQuery(), cancellationToken).ConfigureAwait(false);
        // EbicsEventQuery has no transaction-id dimension, so filter the transaction's events client-side.
        var events = all
            .Where(e => string.Equals(e.TransactionId, transactionIdHex, StringComparison.Ordinal))
            .ToList();

        var captures = await _captures.GetAsync(transactionIdHex, cancellationToken).ConfigureAwait(false);
        var resident = TryResolveResident(transactionIdHex, out var orderData);

        if (events.Count == 0 && captures.Count == 0 && !resident)
        {
            return null;
        }

        var summary = await BuildSummaryAsync(transactionIdHex, events, cancellationToken).ConfigureAwait(false);

        return new TransactionDetail
        {
            Summary = summary,
            Events = events.Select(ToEventView).ToList(),
            Messages = captures.Select(ToCapturedMessageView).ToList(),
            OrderData = orderData is null ? null : BuildOrderDataView(orderData),
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventView>> GetEventsAsync(EventLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = new EbicsEventQuery
        {
            PartnerId = PartnerId.TryCreate(filter.Partner, out var partner) ? partner : null,
            Type = filter.Type,
            From = filter.From,
            To = filter.To,
        };

        var events = await _eventLog.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        // Severity is not a query dimension, so filter it here.
        IEnumerable<EbicsEvent> matched = filter.Severity is { } severity
            ? events.Where(e => e.Severity == severity)
            : events;

        return matched.Select(ToEventView).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default)
    {
        var events = await _eventLog.QueryAsync(new EbicsEventQuery(), cancellationToken).ConfigureAwait(false);

        return events
            .Select(e => e.PartnerId?.Value)
            .Where(v => v is not null)
            .Select(v => v!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
    }

    // Groups the transaction-bearing events by their (hex) transaction id, preserving first-seen order and
    // ascending sequence within each group.
    private static Dictionary<string, List<EbicsEvent>> GroupByTransaction(IReadOnlyList<EbicsEvent> events)
    {
        var groups = new Dictionary<string, List<EbicsEvent>>(StringComparer.Ordinal);
        foreach (var evt in events)
        {
            if (evt.TransactionId is not { } id)
            {
                continue;
            }

            if (!groups.TryGetValue(id, out var list))
            {
                list = [];
                groups[id] = list;
            }

            list.Add(evt);
        }

        return groups;
    }

    private async Task<TransactionSummary> BuildSummaryAsync(string transactionIdHex, List<EbicsEvent> events, CancellationToken ct)
    {
        var resident = TryResolveResidentSummary(transactionIdHex, out var numSegments);
        var hasCapture = (await _captures.GetAsync(transactionIdHex, ct).ConfigureAwait(false)).Count > 0;

        var createdAt = events.Count > 0 ? events[0].Timestamp : DateTimeOffset.MinValue;
        var lastActivityAt = events.Count > 0 ? events[^1].Timestamp : createdAt;

        var lastReturnCode = events
            .Where(e => e.ReturnCode is not null)
            .Select(e => e.ReturnCode)
            .LastOrDefault();

        return new TransactionSummary
        {
            TransactionIdHex = transactionIdHex,
            Kind = DeriveKind(events),
            OrderType = events.Select(e => e.OrderType).FirstOrDefault(o => o is not null),
            HostLabel = events.Select(e => e.HostId?.Value).FirstOrDefault(v => v is not null),
            PartnerLabel = events.Select(e => e.PartnerId?.Value).FirstOrDefault(v => v is not null),
            UserLabel = events.Select(e => e.UserId?.Value).FirstOrDefault(v => v is not null),
            NumSegments = numSegments,
            Status = DeriveStatus(events),
            LastReturnCode = lastReturnCode is { } rc ? $"{rc.Code} {rc.SymbolicName}" : null,
            CreatedAt = createdAt,
            LastActivityAt = lastActivityAt,
            EventCount = events.Count,
            HasCapture = hasCapture,
            IsResident = resident,
        };
    }

    private static TransactionKind DeriveKind(List<EbicsEvent> events)
    {
        var types = events.Select(e => e.Type).ToHashSet();
        if (types.Contains(EbicsEventType.UploadStarted) || types.Contains(EbicsEventType.UploadCompleted))
        {
            return TransactionKind.Upload;
        }

        if (types.Contains(EbicsEventType.DownloadStarted)
            || types.Contains(EbicsEventType.DownloadCompleted)
            || types.Contains(EbicsEventType.ReceiptNegative))
        {
            return TransactionKind.Download;
        }

        var orderType = events.Select(e => e.OrderType).FirstOrDefault(o => o is not null);
        if (orderType is not null && UploadOrderTypes.Contains(orderType))
        {
            return TransactionKind.Upload;
        }

        if (orderType is not null && DownloadOrderTypes.Contains(orderType))
        {
            return TransactionKind.Download;
        }

        return TransactionKind.Unknown;
    }

    private static TransactionStatus DeriveStatus(List<EbicsEvent> events)
    {
        if (events.Any(e => e.Type == EbicsEventType.TransactionEvicted))
        {
            return TransactionStatus.Evicted;
        }

        if (events.Any(e => e.Type is EbicsEventType.UploadCompleted or EbicsEventType.DownloadCompleted))
        {
            return TransactionStatus.Completed;
        }

        var failed = events.Any(e =>
            e.Type == EbicsEventType.ReceiptNegative
            || e.Severity is EbicsEventSeverity.Warning or EbicsEventSeverity.Error);
        return failed ? TransactionStatus.Failed : TransactionStatus.Running;
    }

    // Store lookup for the detail view: also yields the decrypted order data when available.
    private bool TryResolveResident(string transactionIdHex, out byte[]? orderData)
    {
        if (_uploads.TryGet(transactionIdHex, out var upload) && upload is not null)
        {
            orderData = upload.OrderData; // null until the upload is complete
            return true;
        }

        if (_downloads.TryGet(transactionIdHex, out var download) && download is not null)
        {
            orderData = download.OrderDataPlaintext;
            return true;
        }

        orderData = null;
        return false;
    }

    // Store lookup for the summary: residency plus the announced segment count.
    private bool TryResolveResidentSummary(string transactionIdHex, out int? numSegments)
    {
        if (_uploads.TryGet(transactionIdHex, out var upload) && upload is not null)
        {
            numSegments = upload.NumSegments;
            return true;
        }

        if (_downloads.TryGet(transactionIdHex, out var download) && download is not null)
        {
            numSegments = download.NumSegments;
            return true;
        }

        numSegments = null;
        return false;
    }

    private static EventView ToEventView(EbicsEvent e) => new()
    {
        Sequence = e.Sequence,
        Timestamp = e.Timestamp,
        Type = e.Type,
        Severity = e.Severity,
        Visibility = e.Visibility,
        HostLabel = e.HostId?.Value,
        PartnerLabel = e.PartnerId?.Value,
        UserLabel = e.UserId?.Value,
        OrderType = e.OrderType,
        TransactionIdHex = e.TransactionId,
        ReturnCode = e.ReturnCode is { } rc ? $"{rc.Code} {rc.SymbolicName}" : null,
        Message = e.Message,
    };

    private static CapturedMessageView ToCapturedMessageView(CapturedMessage m) => new()
    {
        Phase = m.Phase,
        SegmentNumber = m.SegmentNumber,
        Timestamp = m.Timestamp,
        RequestXml = m.RequestXml,
        ResponseXml = m.ResponseXml,
        RequestTruncated = m.RequestTruncated,
        ResponseTruncated = m.ResponseTruncated,
    };

    private static OrderDataView BuildOrderDataView(byte[] data)
    {
        var truncated = data.Length > MaxOrderDataPreviewBytes;
        var slice = truncated ? data.AsSpan(0, MaxOrderDataPreviewBytes) : data.AsSpan();
        var isText = IsProbablyText(slice);

        return new OrderDataView
        {
            ByteLength = data.Length,
            IsText = isText,
            Truncated = truncated,
            Text = isText ? Encoding.UTF8.GetString(slice) : string.Empty,
            Hex = Convert.ToHexString(slice),
        };
    }

    // Heuristic: text unless the byte stream contains control characters other than tab/LF/CR. High bytes
    // are allowed (UTF-8 multibyte sequences), so UTF-8 documents (pain.001, camt, MT940) read as text.
    private static bool IsProbablyText(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            if (b is 0x09 or 0x0A or 0x0D)
            {
                continue;
            }

            if (b < 0x20)
            {
                return false;
            }
        }

        return true;
    }
}
