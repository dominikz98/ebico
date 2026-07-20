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

namespace EBICO.Tests.Connector.Upload;

/// <summary>Configures the <see cref="FakeUploadServer"/> for a test (failure injection).</summary>
internal sealed class FakeUploadServerOptions
{
    /// <summary>The return code the initialisation phase reports (default EBICS_OK).</summary>
    public EbicsReturnCode InitReturnCode { get; init; } = EbicsReturnCode.Ok;

    /// <summary>When set, the code the <em>last</em> transfer segment reports (e.g. an invalid-payment rejection).</summary>
    public EbicsReturnCode? LastTransferReturnCode { get; init; }

    /// <summary>Whether the initialisation response omits the transaction id (a malformed server response).</summary>
    public bool OmitTransactionId { get; init; }
}

/// <summary>
/// A Tier-A stand-in for the EBICS upload server: a <see cref="FakeTransport"/> responder that drives the
/// two-phase transaction with the committed per-version bindings. On initialisation it captures the
/// encrypted transaction key, the ES and the announced segment count and assigns a fixed transaction id;
/// on transfer it captures each order-data segment. Responses are built with the real
/// <see cref="EbicsResponseFactory"/>, so the harness exercises the exact wire form the server produces.
/// </summary>
internal sealed class FakeUploadServer
{
    private static readonly byte[] FixedTransactionId =
        [0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F];

    private readonly EbicsVersion _version;
    private readonly FakeUploadServerOptions _options;
    private readonly EbicsResponseFactory _factory = new();
    private readonly SortedDictionary<ulong, byte[]> _segments = [];

    /// <summary>Initializes the fake server.</summary>
    /// <param name="version">The EBICS version being exercised.</param>
    /// <param name="options">The failure-injection options.</param>
    public FakeUploadServer(EbicsVersion version, FakeUploadServerOptions options)
    {
        _version = version;
        _options = options;
    }

    /// <summary>The fixed 16-byte transaction id assigned in the initialisation response.</summary>
    public byte[] TransactionId => FixedTransactionId;

    /// <summary>The number of initialisation requests received.</summary>
    public int InitRequestCount { get; private set; }

    /// <summary>The number of transfer requests received.</summary>
    public int TransferRequestCount { get; private set; }

    /// <summary>The encrypted transaction key captured from the initialisation request.</summary>
    public byte[]? EncryptedTransactionKey { get; private set; }

    /// <summary>The ES (SignatureData) blob captured from the initialisation request.</summary>
    public byte[]? SignatureData { get; private set; }

    /// <summary>The announced <c>NumSegments</c> captured from the initialisation request.</summary>
    public ulong NumSegments { get; private set; }

    /// <summary>The header order/admin-order type captured from the initialisation request (e.g. <c>"CCT"</c>, <c>"BTU"</c>).</summary>
    public string? HeaderOrderType { get; private set; }

    /// <summary>The H003/H004 order attribute captured from the initialisation request (e.g. <c>"Dzhnn"</c>).</summary>
    public string? OrderAttribute { get; private set; }

    /// <summary>The H005 BTF captured from the initialisation request, or <see langword="null"/>.</summary>
    public BusinessTransactionFormat? Btf { get; private set; }

    /// <summary>The transferred order-data segments in segment-number order.</summary>
    public IReadOnlyList<byte[]> OrderedSegments => [.. _segments.Values];

    /// <summary>Responds to a request as the upload server would (used as the <see cref="FakeTransport"/> responder).</summary>
    /// <param name="request">The client request.</param>
    /// <returns>The server response.</returns>
    public EbicsHttpResponse Respond(EbicsHttpRequest request)
    {
        var xml = Encoding.UTF8.GetString(request.Payload.Span);
        var parsed = Parse(xml);

        if (IsInitialisation(parsed))
        {
            InitRequestCount++;
            CaptureInit(parsed);
            var transactionId = _options.OmitTransactionId ? null : TransactionId;
            return Serialize(_factory.BuildTransactionResponse(
                _version, EbicsTransactionPhase.Initialisation, transactionId, _options.InitReturnCode));
        }

        TransferRequestCount++;
        var (segmentNumber, lastSegment, orderData) = ReadTransfer(parsed);
        _segments[segmentNumber] = orderData;
        var code = lastSegment && _options.LastTransferReturnCode is { } configured ? configured : EbicsReturnCode.Ok;
        return Serialize(_factory.BuildTransactionResponse(
            _version, EbicsTransactionPhase.Transfer, TransactionId, code, segmentNumber, lastSegment));
    }

    private object Parse(string xml) => _version switch
    {
        EbicsVersion.H003 => EbicsXmlSerializer.Deserialize<H3.EbicsRequest>(xml),
        EbicsVersion.H004 => EbicsXmlSerializer.Deserialize<H4.EbicsRequest>(xml),
        _ => EbicsXmlSerializer.Deserialize<H5.EbicsRequest>(xml),
    };

    private static bool IsInitialisation(object request) => request switch
    {
        H3.EbicsRequest r => r.Header?.Mutable?.TransactionPhase == H3.TransactionPhaseType.Initialisation,
        H4.EbicsRequest r => r.Header?.Mutable?.TransactionPhase == H4.TransactionPhaseType.Initialisation,
        H5.EbicsRequest r => r.Header?.Mutable?.TransactionPhase == H5.TransactionPhaseType.Initialisation,
        _ => false,
    };

    private void CaptureInit(object request)
    {
        switch (request)
        {
            case H3.EbicsRequest r:
                NumSegments = r.Header?.Static?.NumSegments ?? 0;
                EncryptedTransactionKey = r.Body?.DataTransfer?.DataEncryptionInfo?.TransactionKey;
                SignatureData = r.Body?.DataTransfer?.SignatureData?.Value;
                HeaderOrderType = r.Header?.Static?.OrderDetails?.OrderType?.Value;
                OrderAttribute = r.Header?.Static?.OrderDetails?.OrderAttribute.ToString();
                break;
            case H4.EbicsRequest r:
                NumSegments = r.Header?.Static?.NumSegments ?? 0;
                EncryptedTransactionKey = r.Body?.DataTransfer?.DataEncryptionInfo?.TransactionKey;
                SignatureData = r.Body?.DataTransfer?.SignatureData?.Value;
                HeaderOrderType = r.Header?.Static?.OrderDetails?.OrderType?.Value;
                OrderAttribute = r.Header?.Static?.OrderDetails?.OrderAttribute.ToString();
                break;
            case H5.EbicsRequest r:
                NumSegments = r.Header?.Static?.NumSegments ?? 0;
                EncryptedTransactionKey = r.Body?.DataTransfer?.DataEncryptionInfo?.TransactionKey;
                SignatureData = r.Body?.DataTransfer?.SignatureData?.Value;
                HeaderOrderType = r.Header?.Static?.OrderDetails?.AdminOrderType?.Value;
                if (r.Header?.Static?.OrderDetails?.OrderParams is H5.BtuParamsType btu
                    && BusinessTransactionFormat.TryFromBtfParams(btu, out var btf))
                {
                    Btf = btf;
                }

                break;
        }
    }

    private static (ulong SegmentNumber, bool LastSegment, byte[] OrderData) ReadTransfer(object request) => request switch
    {
        H3.EbicsRequest r => (
            r.Header!.Mutable!.SegmentNumber!.Value,
            r.Header.Mutable.SegmentNumber.LastSegment,
            r.Body!.DataTransfer!.OrderData!.Value),
        H4.EbicsRequest r => (
            r.Header!.Mutable!.SegmentNumber!.Value,
            r.Header.Mutable.SegmentNumber.LastSegment,
            r.Body!.DataTransfer!.OrderData!.Value),
        H5.EbicsRequest r => (
            r.Header!.Mutable!.SegmentNumber!.Value,
            r.Header.Mutable.SegmentNumber.LastSegment,
            r.Body!.DataTransfer!.OrderData!.Value),
        _ => throw new InvalidOperationException("Unknown request type."),
    };

    private static EbicsHttpResponse Serialize(EBICO.Core.Versioning.IEbicsResponseEnvelope envelope)
        => new() { StatusCode = 200, Payload = EbicsXmlSerializer.SerializeToUtf8Bytes(envelope) };
}

/// <summary>
/// Test harness for the upload handlers: wires a connector service provider with a
/// <see cref="FakeUploadServer"/> transport and provisions the subscriber (signature/authentication) and
/// bank (encryption) keys the upload flow requires. Exposes a round-trip decode that mirrors the server's
/// <c>FinalizeOrderAsync</c> (reassemble → decrypt → decompress) to prove the uploaded bytes are recoverable.
/// </summary>
internal sealed class UploadTestHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    private UploadTestHarness(
        EbicsVersion version,
        FakeUploadServer server,
        IEbicsClient client,
        RsaKeyMaterial bankEncryptionKeyPair,
        KeyVersion encryptionVersion,
        ServiceProvider provider)
    {
        Version = version;
        Server = server;
        Client = client;
        BankEncryptionKeyPair = bankEncryptionKeyPair;
        EncryptionVersion = encryptionVersion;
        _provider = provider;
    }

    /// <summary>The EBICS version being exercised.</summary>
    public EbicsVersion Version { get; }

    /// <summary>The fake upload server.</summary>
    public FakeUploadServer Server { get; }

    /// <summary>The connector client under test.</summary>
    public IEbicsClient Client { get; }

    /// <summary>The bank encryption key pair (with private key) used to decrypt the uploaded data.</summary>
    public RsaKeyMaterial BankEncryptionKeyPair { get; }

    /// <summary>The bank encryption key version.</summary>
    public KeyVersion EncryptionVersion { get; }

    /// <summary>Builds a harness for <paramref name="version"/> with fully provisioned keys.</summary>
    /// <param name="version">The EBICS version.</param>
    /// <param name="options">The fake-server failure-injection options, or <see langword="null"/> for the happy path.</param>
    /// <param name="allowedOrderTypes">An optional client-side allow-list of order-type codes; <see langword="null"/> leaves it empty (no client-side check).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The initialized harness.</returns>
    public static async Task<UploadTestHarness> CreateAsync(
        EbicsVersion version,
        FakeUploadServerOptions? options = null,
        IEnumerable<string>? allowedOrderTypes = null,
        CancellationToken ct = default)
    {
        var bankEncryptionKeyPair = RsaKeyMaterial.Generate();
        var server = new FakeUploadServer(version, options ?? new FakeUploadServerOptions());
        var transport = new FakeTransport(server.Respond);

        var services = new ServiceCollection();
        services.AddEbicoConnector(o =>
        {
            o.Url = "https://bank.example/ebics";
            o.HostId = "HOST";
            o.PartnerId = "PART";
            o.UserId = "USER";
            o.Version = version;
            foreach (var code in allowedOrderTypes ?? [])
            {
                o.AllowedOrderTypes.Add(code);
            }
        });
        services.AddEbicoUpload();
        services.RemoveAll<ITransport>();
        services.AddSingleton<ITransport>(transport);
        var provider = services.BuildServiceProvider();

        var keys = provider.GetRequiredService<IKeyStore>();
        await keys.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Signature, RsaKeyMaterial.Generate(), ct);
        await keys.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Authentication, RsaKeyMaterial.Generate(), ct);
        await keys.StoreAsync(KeyOwner.Bank, KeyPurpose.Encryption, bankEncryptionKeyPair.ToPublicOnly(), ct);

        var encryptionVersion = KeyVersions.Default(KeyPurpose.Encryption, version).Version;
        var client = provider.GetRequiredService<IEbicsClient>();
        return new UploadTestHarness(version, server, client, bankEncryptionKeyPair, encryptionVersion, provider);
    }

    /// <summary>
    /// Decodes the order data the client uploaded exactly as the server would: reassembles the transferred
    /// segments, decrypts the transaction key with the bank's private encryption key, decrypts the order
    /// data and decompresses it.
    /// </summary>
    /// <returns>The recovered order-data plaintext.</returns>
    public byte[] DecodeUploadedOrderData()
    {
        var ciphertext = EbicsSegmentation.Reassemble(Server.OrderedSegments);
        var transactionKey = EncryptionE002.DecryptTransactionKey(
            Server.EncryptedTransactionKey!, BankEncryptionKeyPair, EncryptionVersion);
        var compressed = EncryptionE002.DecryptOrderData(ciphertext, transactionKey);
        return EbicsCompression.Decompress(compressed);
    }

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();
}
