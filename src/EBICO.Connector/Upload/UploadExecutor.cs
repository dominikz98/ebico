using EBICO.Connector.Upload.Envelopes;
using EBICO.Connector.Validation;
using EBICO.Core;
using EBICO.Core.Administrative;
using EBICO.Core.Btf;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Upload;

/// <summary>
/// Executes the client side of the two-phase EBICS upload transaction (initialisation → transfer),
/// shared by the generic and the convenience upload handlers. It performs the client-side crypto
/// (compress → E002-encrypt → electronically sign → segment), builds and X002-signs the version-specific
/// envelopes and interprets the server return codes into an <see cref="EbicsResult{T}"/>.
/// </summary>
internal sealed class UploadExecutor
{
    /// <summary>
    /// The default maximum raw (pre-base64) segment size — the shared EBICO default
    /// (<see cref="EbicsSegmentation.DefaultSegmentSizeBytes"/>, 512 KiB), which is also what
    /// <c>EbicoServerOptions.SegmentSizeBytes</c> uses.
    /// </summary>
    /// <remarks>
    /// Must stay in sync with the server default: a full segment's base64 form plus the surrounding
    /// envelope has to fit into the peer's request-body limit. The previous 768 KiB base64-encoded to
    /// exactly 1 MiB — the emulator's default body limit — so every full segment was rejected with
    /// HTTP 413 before the server could answer at all (#124). Callers that talk to a peer with a
    /// different limit should pass an explicit size derived from
    /// <see cref="EbicsSegmentation.MaxSegmentSizeForRequestBody"/>.
    /// </remarks>
    private const int DefaultMaxSegmentSizeBytes = EbicsSegmentation.DefaultSegmentSizeBytes;

    private readonly IUploadEnvelopeBuilderRegistry _registry;

    /// <summary>Initializes the executor.</summary>
    /// <param name="registry">The version-specific envelope builder registry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
    public UploadExecutor(IUploadEnvelopeBuilderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>Runs the upload transaction and returns its business result.</summary>
    /// <param name="orderData">The order payload to upload.</param>
    /// <param name="orderType">The order type (see <see cref="UploadRequest.OrderType"/>).</param>
    /// <param name="btf">The H005 business transaction format, or <see langword="null"/>.</param>
    /// <param name="fileFormat">The H003/H004 FUL file format, or <see langword="null"/>.</param>
    /// <param name="maxSegmentSizeBytes">The maximum raw segment size, or <see langword="null"/> for the default.</param>
    /// <param name="ctx">The per-send execution context.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="distributedSignature">Whether to ask the bank to park the order for the distributed electronic signature (see <see cref="UploadRequest.DistributedSignature"/>).</param>
    /// <param name="veu">The referenced parked order for the VEU uploads HVE/HVS, or <see langword="null"/>.</param>
    /// <returns>The upload result, or a failure carrying the server return code.</returns>
    /// <exception cref="EbicsConfigurationException">The order identity is incomplete, the segment size is not positive, a VEU reference is missing or a required key is missing.</exception>
    public async Task<EbicsResult<UploadResult>> ExecuteAsync(
        ReadOnlyMemory<byte> orderData,
        string? orderType,
        BusinessTransactionFormat? btf,
        string? fileFormat,
        int? maxSegmentSizeBytes,
        EbicsContext ctx,
        CancellationToken ct,
        bool distributedSignature = false,
        VeuOrderReference? veu = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var connection = ctx.Connection;

        // Pipeline stage 1: validate (structural/BTF always; authorisation opt-in) before any key I/O or
        // crypto, so a malformed or unauthorised request fails fast without a server round-trip.
        var validation = RequestValidator.ValidateUpload(
            connection, orderData, maxSegmentSizeBytes, orderType, btf, fileFormat);
        if (!validation.IsAuthorized)
        {
            return EbicsResult<UploadResult>.Failure(validation.ReturnCode, validation.ReturnText);
        }

        var (headerOrderType, effectiveBtf, effectiveFileFormat, effectiveOrderType) = validation.Identity;

        // HVE/HVS act on one specific parked order; without its id the bank can only answer
        // 091121 EBICS_INVALID_ORDER_IDENTIFIER, so fail fast with a message that says what is missing (#124).
        if (VeuOrderTypes.IsVeuUploadOrderType(effectiveOrderType) && veu is null)
        {
            throw new EbicsConfigurationException(
                $"The VEU upload '{effectiveOrderType}' must reference the parked order it acts on; "
                    + $"set {nameof(UploadRequest)}.{nameof(UploadRequest.Veu)}.");
        }

        var segmentSize = maxSegmentSizeBytes ?? DefaultMaxSegmentSizeBytes;
        var version = connection.Version;
        var builder = _registry.Get(version);

        var encVersion = KeyVersions.Default(KeyPurpose.Encryption, version).Version;
        var sigVersion = KeyVersions.Default(KeyPurpose.Signature, version).Version;
        var authVersion = KeyVersions.Default(KeyPurpose.Authentication, version).Version;

        var bankEncKey = await UploadSupport.RequireBankKeyAsync(ctx, KeyPurpose.Encryption, ct).ConfigureAwait(false);
        var signatureKey = await UploadSupport.RequireSubscriberKeyAsync(ctx, KeyPurpose.Signature, ct).ConfigureAwait(false);
        var authKey = await UploadSupport.RequireSubscriberKeyAsync(ctx, KeyPurpose.Authentication, ct).ConfigureAwait(false);

        // Synchronous crypto stage (kept in a non-async helper so the Span<byte> locals stay legal).
        var prepared = Prepare(
            version, orderData, bankEncKey, encVersion, signatureKey, sigVersion,
            connection.PartnerId.Value, connection.UserId.Value, segmentSize);

        // Initialisation phase.
        var initContext = new UploadInitContext(
            connection.HostId.Value,
            connection.PartnerId.Value,
            connection.UserId.Value,
            headerOrderType,
            effectiveBtf,
            effectiveFileFormat,
            (ulong)prepared.Segmented.NumSegments,
            prepared.EncryptedTransactionKey,
            prepared.EncryptionPubKeyDigest,
            encVersion.Value,
            prepared.SignatureData,
            distributedSignature,
            veu);

        var initEnvelope = builder.BuildInitRequest(initContext);
        Sign(initEnvelope, authKey, authVersion);
        var initResponseXml = await UploadSupport.ExchangeAsync(initEnvelope, ctx, ct).ConfigureAwait(false);
        var initView = builder.ParseInitResponse(initResponseXml);
        if (initView.ReturnCode != EbicsResult<UploadResult>.OkReturnCode)
        {
            return EbicsResult<UploadResult>.Failure(initView.ReturnCode, initView.ReportText);
        }

        if (initView.TransactionId is not { } transactionId)
        {
            throw new EbicsConnectorException(
                "The upload initialisation reported success but the response carried no transaction id.");
        }

        // Transfer phase: one message per segment.
        var segments = prepared.Segmented.Segments;
        for (var i = 0; i < segments.Count; i++)
        {
            var transferContext = new UploadTransferContext(
                connection.HostId.Value, transactionId, (ulong)(i + 1), i == segments.Count - 1, segments[i]);
            var transferEnvelope = builder.BuildTransferRequest(transferContext);
            Sign(transferEnvelope, authKey, authVersion);
            var transferResponseXml = await UploadSupport.ExchangeAsync(transferEnvelope, ctx, ct).ConfigureAwait(false);
            var transferView = builder.ParseTransferResponse(transferResponseXml);
            if (transferView.ReturnCode != EbicsResult<UploadResult>.OkReturnCode)
            {
                return EbicsResult<UploadResult>.Failure(transferView.ReturnCode, transferView.ReportText);
            }
        }

        return EbicsResult<UploadResult>.Success(new UploadResult
        {
            TransactionId = Convert.ToHexString(transactionId),
            NumSegments = prepared.Segmented.NumSegments,
        });
    }

    // Compresses, E002-encrypts and electronically signs the order data and segments the ciphertext. The
    // order-data ciphertext and the ES both use the same one-time transaction key (the EBICS convention).
    private static PreparedUpload Prepare(
        EbicsVersion version,
        ReadOnlyMemory<byte> orderData,
        RsaKeyMaterial bankEncKey,
        KeyVersion encVersion,
        RsaKeyMaterial signatureKey,
        KeyVersion sigVersion,
        string partnerId,
        string userId,
        int segmentSize)
    {
        var span = orderData.Span;
        var compressed = EbicsCompression.Compress(span);
        var transactionKey = EncryptionE002.GenerateTransactionKey();
        var encryptedTransactionKey = EncryptionE002.EncryptTransactionKey(transactionKey, bankEncKey, encVersion);
        var encryptedOrderData = EncryptionE002.EncryptOrderData(compressed, transactionKey);
        var encryptionDigest = PublicKeyFingerprint.Compute(bankEncKey);

        var userSignature = UserSignatureDataAssembler.Build(version, span, signatureKey, sigVersion, partnerId, userId);
        var signatureData = EncryptionE002.EncryptOrderData(EbicsCompression.Compress(userSignature), transactionKey);

        var segmented = EbicsSegmentation.Split(encryptedOrderData, segmentSize);
        return new PreparedUpload(encryptedTransactionKey, encryptionDigest, signatureData, segmented);
    }

    private static void Sign(IAuthSignedRequestEnvelope envelope, RsaKeyMaterial authKey, KeyVersion authVersion)
    {
        var unsignedXml = EbicsXmlSerializer.SerializeToString(envelope);
        envelope.AuthSignature = AuthenticationSignature.Sign(unsignedXml, authKey, authVersion);
    }

    // The artefacts of the synchronous crypto stage carried into the async transaction loop.
    private readonly record struct PreparedUpload(
        byte[] EncryptedTransactionKey,
        byte[] EncryptionPubKeyDigest,
        byte[] SignatureData,
        SegmentedOrderData Segmented);
}
