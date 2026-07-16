using System.Text;
using EBICO.Connector;
using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Crypto;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Server.ReturnCodes;
using EBICO.Server.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using H3 = EBICO.Core.Schema.H003;
using H4 = EBICO.Core.Schema.H004;
using H5 = EBICO.Core.Schema.H005;

namespace EBICO.Tests.Connector.Download;

/// <summary>Configures the <see cref="FakeDownloadServer"/> for a test (failure injection).</summary>
internal sealed class FakeDownloadServerOptions
{
    /// <summary>The return code the initialisation phase reports (default EBICS_OK).</summary>
    public EbicsReturnCode InitReturnCode { get; init; } = EbicsReturnCode.Ok;

    /// <summary>When set, the code the <em>first</em> transfer segment reports (e.g. an unknown transaction id).</summary>
    public EbicsReturnCode? TransferReturnCode { get; init; }

    /// <summary>The raw (pre-base64) segment size the server splits the ciphertext into (default keeps it a single segment).</summary>
    public int SegmentSizeBytes { get; init; } = 512 * 1024;

    /// <summary>When <see langword="true"/>, the server encrypts the transaction key for a different key so the client cannot decrypt it.</summary>
    public bool EncryptForWrongKey { get; init; }
}

/// <summary>
/// A Tier-A stand-in for the EBICS download server: a <see cref="FakeTransport"/> responder that drives
/// the three-phase transaction with the committed per-version bindings. On initialisation it encodes the
/// seeded plaintext exactly as the server would (compress → E002-encrypt for the subscriber → segment),
/// captures the requested order identity and reporting period and answers with segment 1 plus the
/// <c>DataEncryptionInfo</c>; on transfer it serves the remaining segments; on receipt it captures the
/// receipt code. Responses are built with the real <see cref="EbicsResponseFactory"/>, so the harness
/// exercises the exact wire form the server produces.
/// </summary>
internal sealed class FakeDownloadServer
{
    private static readonly byte[] FixedTransactionId =
        [0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F];

    private readonly EbicsVersion _version;
    private readonly byte[] _plaintext;
    private readonly RsaKeyMaterial _subscriberEncryptionPublicKey;
    private readonly KeyVersion _encryptionVersion;
    private readonly FakeDownloadServerOptions _options;
    private readonly EbicsResponseFactory _factory = new();

    private IReadOnlyList<byte[]> _segments = [];
    private byte[] _encryptedTransactionKey = [];
    private byte[] _encryptionDigest = [];

    /// <summary>Initializes the fake server.</summary>
    /// <param name="version">The EBICS version being exercised.</param>
    /// <param name="plaintext">The order-data plaintext the server delivers.</param>
    /// <param name="subscriberEncryptionPublicKey">The subscriber's public encryption key the data is encrypted for.</param>
    /// <param name="encryptionVersion">The encryption key version.</param>
    /// <param name="options">The failure-injection options.</param>
    public FakeDownloadServer(
        EbicsVersion version,
        byte[] plaintext,
        RsaKeyMaterial subscriberEncryptionPublicKey,
        KeyVersion encryptionVersion,
        FakeDownloadServerOptions options)
    {
        _version = version;
        _plaintext = plaintext;
        _subscriberEncryptionPublicKey = subscriberEncryptionPublicKey;
        _encryptionVersion = encryptionVersion;
        _options = options;
    }

    /// <summary>The fixed 16-byte transaction id assigned in the initialisation response.</summary>
    public byte[] TransactionId => FixedTransactionId;

    /// <summary>The number of initialisation requests received.</summary>
    public int InitRequestCount { get; private set; }

    /// <summary>The number of transfer requests received.</summary>
    public int TransferRequestCount { get; private set; }

    /// <summary>The number of receipt requests received.</summary>
    public int ReceiptRequestCount { get; private set; }

    /// <summary>The header order/admin-order type captured from the initialisation request (e.g. <c>"STA"</c>, <c>"BTD"</c>, <c>"HTD"</c>).</summary>
    public string? HeaderOrderType { get; private set; }

    /// <summary>The H005 BTF captured from the initialisation request, or <see langword="null"/>.</summary>
    public BusinessTransactionFormat? Btf { get; private set; }

    /// <summary>The reporting-period start captured from the initialisation request, or <see langword="null"/>.</summary>
    public DateOnly? PeriodStart { get; private set; }

    /// <summary>The reporting-period end captured from the initialisation request, or <see langword="null"/>.</summary>
    public DateOnly? PeriodEnd { get; private set; }

    /// <summary>The receipt code captured from the receipt request, or <see langword="null"/>.</summary>
    public byte? ReceiptCode { get; private set; }

    /// <summary>Responds to a request as the download server would (used as the <see cref="FakeTransport"/> responder).</summary>
    /// <param name="request">The client request.</param>
    /// <returns>The server response.</returns>
    public EbicsHttpResponse Respond(EbicsHttpRequest request)
    {
        var parsed = Parse(Encoding.UTF8.GetString(request.Payload.Span));
        return Phase(parsed) switch
        {
            EbicsTransactionPhase.Initialisation => RespondInit(parsed),
            EbicsTransactionPhase.Transfer => RespondTransfer(parsed),
            _ => RespondReceipt(parsed),
        };
    }

    private EbicsHttpResponse RespondInit(object request)
    {
        InitRequestCount++;
        CaptureInit(request);

        if (_options.InitReturnCode.Code != EbicsReturnCode.OkCode)
        {
            return Serialize(_factory.BuildDownloadResponse(
                _version, new DownloadTransactionResult(_options.InitReturnCode, EbicsTransactionPhase.Initialisation)));
        }

        EncodePayload();
        var lastSegment = _segments.Count == 1;
        var firstSegment = new DownloadSegmentPayload(_segments[0], _encryptedTransactionKey, _encryptionDigest, _encryptionVersion);
        return Serialize(_factory.BuildDownloadResponse(
            _version,
            new DownloadTransactionResult(
                EbicsReturnCode.Ok,
                EbicsTransactionPhase.Initialisation,
                FixedTransactionId,
                (ulong)_segments.Count,
                SegmentNumber: 1,
                LastSegment: lastSegment,
                Segment: firstSegment)));
    }

    private EbicsHttpResponse RespondTransfer(object request)
    {
        TransferRequestCount++;
        var segmentNumber = ReadSegmentNumber(request);

        if (_options.TransferReturnCode is { } configured)
        {
            return Serialize(_factory.BuildDownloadResponse(
                _version, new DownloadTransactionResult(configured, EbicsTransactionPhase.Transfer, FixedTransactionId, SegmentNumber: segmentNumber)));
        }

        var lastSegment = segmentNumber == (ulong)_segments.Count;
        var payload = new DownloadSegmentPayload(_segments[(int)segmentNumber - 1]);
        return Serialize(_factory.BuildDownloadResponse(
            _version,
            new DownloadTransactionResult(
                EbicsReturnCode.Ok, EbicsTransactionPhase.Transfer, FixedTransactionId, SegmentNumber: segmentNumber, LastSegment: lastSegment, Segment: payload)));
    }

    private EbicsHttpResponse RespondReceipt(object request)
    {
        ReceiptRequestCount++;
        ReceiptCode = ReadReceiptCode(request);
        var code = ReceiptCode == 0 ? EbicsReturnCode.DownloadPostprocessDone : EbicsReturnCode.DownloadPostprocessSkipped;
        return Serialize(_factory.BuildDownloadResponse(
            _version, new DownloadTransactionResult(code, EbicsTransactionPhase.Receipt, FixedTransactionId)));
    }

    // Mirrors the server data path: compress → E002-encrypt for the subscriber → segment the ciphertext.
    private void EncodePayload()
    {
        var recipient = _options.EncryptForWrongKey ? RsaKeyMaterial.Generate().ToPublicOnly() : _subscriberEncryptionPublicKey;
        var compressed = EbicsCompression.Compress(_plaintext);
        var encrypted = EncryptionE002.Encrypt(compressed, recipient, _encryptionVersion);
        _encryptedTransactionKey = encrypted.EncryptedTransactionKey;
        _encryptionDigest = PublicKeyFingerprint.Compute(recipient);
        _segments = EbicsSegmentation.Split(encrypted.EncryptedOrderDataBytes, _options.SegmentSizeBytes).Segments;
    }

    private void CaptureInit(object request)
    {
        switch (request)
        {
            case H3.EbicsRequest r:
                HeaderOrderType = r.Header?.Static?.OrderDetails?.OrderType?.Value;
                CaptureH3Period(r.Header?.Static?.OrderDetails?.OrderParams);
                break;
            case H4.EbicsRequest r:
                HeaderOrderType = r.Header?.Static?.OrderDetails?.OrderType?.Value;
                CaptureH4Period(r.Header?.Static?.OrderDetails?.OrderParams);
                break;
            case H5.EbicsRequest r:
                HeaderOrderType = r.Header?.Static?.OrderDetails?.AdminOrderType?.Value;
                if (r.Header?.Static?.OrderDetails?.OrderParams is H5.BtdParamsType btd)
                {
                    if (BusinessTransactionFormat.TryFromBtfParams(btd, out var btf))
                    {
                        Btf = btf;
                    }

                    CapturePeriod(btd.DateRange?.Start, btd.DateRange?.End);
                }

                break;
        }
    }

    private void CaptureH3Period(object? orderParams)
    {
        switch (orderParams)
        {
            case H3.FdlOrderParamsType { DateRange: { } d }:
                CapturePeriod(d.Start, d.End);
                break;
            case H3.StandardOrderParamsType { DateRange: { } d }:
                CapturePeriod(d.Start, d.End);
                break;
        }
    }

    private void CaptureH4Period(object? orderParams)
    {
        switch (orderParams)
        {
            case H4.FdlOrderParamsType { DateRange: { } d }:
                CapturePeriod(d.Start, d.End);
                break;
            case H4.StandardOrderParamsType { DateRange: { } d }:
                CapturePeriod(d.Start, d.End);
                break;
        }
    }

    private void CapturePeriod(DateTime? start, DateTime? end)
    {
        if (start is { } s)
        {
            PeriodStart = DateOnly.FromDateTime(s);
        }

        if (end is { } e)
        {
            PeriodEnd = DateOnly.FromDateTime(e);
        }
    }

    private object Parse(string xml) => _version switch
    {
        EbicsVersion.H003 => EbicsXmlSerializer.Deserialize<H3.EbicsRequest>(xml),
        EbicsVersion.H004 => EbicsXmlSerializer.Deserialize<H4.EbicsRequest>(xml),
        _ => EbicsXmlSerializer.Deserialize<H5.EbicsRequest>(xml),
    };

    private static EbicsTransactionPhase Phase(object request) => request switch
    {
        H3.EbicsRequest r => MapPhase(r.Header?.Mutable?.TransactionPhase),
        H4.EbicsRequest r => MapPhase(r.Header?.Mutable?.TransactionPhase),
        H5.EbicsRequest r => MapPhase(r.Header?.Mutable?.TransactionPhase),
        _ => EbicsTransactionPhase.Receipt,
    };

    private static EbicsTransactionPhase MapPhase(H3.TransactionPhaseType? phase) => phase switch
    {
        H3.TransactionPhaseType.Initialisation => EbicsTransactionPhase.Initialisation,
        H3.TransactionPhaseType.Transfer => EbicsTransactionPhase.Transfer,
        _ => EbicsTransactionPhase.Receipt,
    };

    private static EbicsTransactionPhase MapPhase(H4.TransactionPhaseType? phase) => phase switch
    {
        H4.TransactionPhaseType.Initialisation => EbicsTransactionPhase.Initialisation,
        H4.TransactionPhaseType.Transfer => EbicsTransactionPhase.Transfer,
        _ => EbicsTransactionPhase.Receipt,
    };

    private static EbicsTransactionPhase MapPhase(H5.TransactionPhaseType? phase) => phase switch
    {
        H5.TransactionPhaseType.Initialisation => EbicsTransactionPhase.Initialisation,
        H5.TransactionPhaseType.Transfer => EbicsTransactionPhase.Transfer,
        _ => EbicsTransactionPhase.Receipt,
    };

    private static ulong ReadSegmentNumber(object request) => request switch
    {
        H3.EbicsRequest r => r.Header!.Mutable!.SegmentNumber!.Value,
        H4.EbicsRequest r => r.Header!.Mutable!.SegmentNumber!.Value,
        H5.EbicsRequest r => r.Header!.Mutable!.SegmentNumber!.Value,
        _ => throw new InvalidOperationException("Unknown request type."),
    };

    private static byte ReadReceiptCode(object request) => request switch
    {
        H3.EbicsRequest r => r.Body!.TransferReceipt!.ReceiptCode,
        H4.EbicsRequest r => r.Body!.TransferReceipt!.ReceiptCode,
        H5.EbicsRequest r => r.Body!.TransferReceipt!.ReceiptCode,
        _ => throw new InvalidOperationException("Unknown request type."),
    };

    private static EbicsHttpResponse Serialize(EBICO.Core.Versioning.IEbicsResponseEnvelope envelope)
        => new() { StatusCode = 200, Payload = EbicsXmlSerializer.SerializeToUtf8Bytes(envelope) };
}

/// <summary>
/// Test harness for the download handlers: wires a connector service provider with a
/// <see cref="FakeDownloadServer"/> transport and provisions the subscriber encryption key (with its
/// private half, so the connector can decrypt) and authentication key the download flow requires.
/// </summary>
internal sealed class DownloadTestHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    private DownloadTestHarness(FakeDownloadServer server, IEbicsClient client, ServiceProvider provider)
    {
        Server = server;
        Client = client;
        _provider = provider;
    }

    /// <summary>The fake download server.</summary>
    public FakeDownloadServer Server { get; }

    /// <summary>The connector client under test.</summary>
    public IEbicsClient Client { get; }

    /// <summary>Builds a harness for <paramref name="version"/> with fully provisioned keys.</summary>
    /// <param name="version">The EBICS version.</param>
    /// <param name="plaintext">The order-data plaintext the server delivers.</param>
    /// <param name="options">The fake-server failure-injection options, or <see langword="null"/> for the happy path.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The initialized harness.</returns>
    public static async Task<DownloadTestHarness> CreateAsync(
        EbicsVersion version, byte[] plaintext, FakeDownloadServerOptions? options = null, CancellationToken ct = default)
    {
        var encryptionVersion = KeyVersions.Default(KeyPurpose.Encryption, version).Version;
        var subscriberEncryptionKeyPair = RsaKeyMaterial.Generate();
        var server = new FakeDownloadServer(
            version, plaintext, subscriberEncryptionKeyPair.ToPublicOnly(), encryptionVersion, options ?? new FakeDownloadServerOptions());
        var transport = new FakeTransport(server.Respond);

        var services = new ServiceCollection();
        services.AddEbicoConnector(o =>
        {
            o.Url = "https://bank.example/ebics";
            o.HostId = "HOST";
            o.PartnerId = "PART";
            o.UserId = "USER";
            o.Version = version;
        });
        services.AddEbicoDownload();
        services.RemoveAll<ITransport>();
        services.AddSingleton<ITransport>(transport);
        var provider = services.BuildServiceProvider();

        var keys = provider.GetRequiredService<IKeyStore>();
        await keys.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Encryption, subscriberEncryptionKeyPair, ct);
        await keys.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Authentication, RsaKeyMaterial.Generate(), ct);

        var client = provider.GetRequiredService<IEbicsClient>();
        return new DownloadTestHarness(server, client, provider);
    }

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();
}
