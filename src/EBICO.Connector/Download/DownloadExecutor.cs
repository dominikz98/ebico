using EBICO.Connector.Download.Envelopes;
using EBICO.Connector.Validation;
using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Download;

/// <summary>
/// Executes the client side of the three-phase EBICS download transaction (initialisation → transfer →
/// receipt), shared by the generic and the convenience download handlers. It X002-signs the
/// version-specific envelopes, collects the order-data segments, performs the client-side crypto
/// (reassemble → E002-decrypt with the subscriber's private key → decompress), runs an optional parsing
/// hook and acknowledges the transfer with a positive receipt (or a negative one on failure).
/// </summary>
internal sealed class DownloadExecutor
{
    private readonly IDownloadEnvelopeBuilderRegistry _registry;

    /// <summary>Initializes the executor.</summary>
    /// <param name="registry">The version-specific envelope builder registry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
    public DownloadExecutor(IDownloadEnvelopeBuilderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>Runs the download transaction and returns its business result.</summary>
    /// <param name="orderType">The order type (see <see cref="DownloadRequest.OrderType"/>).</param>
    /// <param name="btf">The H005 business transaction format, or <see langword="null"/>.</param>
    /// <param name="fileFormat">The H003/H004 FDL file format, or <see langword="null"/>.</param>
    /// <param name="period">An optional closed reporting period.</param>
    /// <param name="parse">An optional parsing hook applied to the decrypted order data before the receipt.</param>
    /// <param name="ctx">The per-send execution context.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The download result, or a failure carrying the server return code.</returns>
    /// <exception cref="EbicsConfigurationException">The order identity is incomplete or a required key is missing.</exception>
    /// <exception cref="EbicsConnectorException">The server reported success but returned a malformed response, or the data could not be decrypted/decompressed.</exception>
    public async Task<EbicsResult<DownloadResult>> ExecuteAsync(
        string? orderType,
        BusinessTransactionFormat? btf,
        string? fileFormat,
        DateRange? period,
        Func<ReadOnlyMemory<byte>, object?>? parse,
        EbicsContext ctx,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var connection = ctx.Connection;

        // Pipeline stage 1: validate (structural/BTF always; authorisation opt-in) before any key I/O or
        // crypto, so a malformed or unauthorised request fails fast without a server round-trip.
        var validation = RequestValidator.ValidateDownload(connection, orderType, btf, fileFormat);
        if (!validation.IsAuthorized)
        {
            return EbicsResult<DownloadResult>.Failure(validation.ReturnCode, validation.ReturnText);
        }

        var (headerOrderType, effectiveBtf, effectiveFileFormat, _) = validation.Identity;

        var version = connection.Version;
        var builder = _registry.Get(version);

        var encVersion = KeyVersions.Default(KeyPurpose.Encryption, version).Version;
        var authVersion = KeyVersions.Default(KeyPurpose.Authentication, version).Version;

        // A download only needs subscriber keys: the response is E002-encrypted for the subscriber's
        // encryption key (decrypted here with its private half) and the requests are X002-signed.
        var encKey = await DownloadSupport.RequireSubscriberKeyAsync(ctx, KeyPurpose.Encryption, ct).ConfigureAwait(false);
        var authKey = await DownloadSupport.RequireSubscriberKeyAsync(ctx, KeyPurpose.Authentication, ct).ConfigureAwait(false);

        // Initialisation phase: request the download and receive NumSegments + segment 1 + the E002
        // encryption info.
        var initEnvelope = builder.BuildInitRequest(new DownloadInitContext(
            connection.HostId.Value,
            connection.PartnerId.Value,
            connection.UserId.Value,
            headerOrderType,
            effectiveBtf,
            effectiveFileFormat,
            period));
        var initView = builder.ParseInitResponse(await SignAndExchangeAsync(initEnvelope).ConfigureAwait(false));
        if (initView.ReturnCode != EbicsResult<DownloadResult>.OkReturnCode)
        {
            return EbicsResult<DownloadResult>.Failure(initView.ReturnCode, initView.ReportText);
        }

        if (initView.TransactionId is not { } transactionId)
        {
            throw new EbicsConnectorException(
                "The download initialisation reported success but the response carried no transaction id.");
        }

        if (initView.NumSegments is not { } numSegments || numSegments == 0)
        {
            throw new EbicsConnectorException(
                "The download initialisation reported success but announced no order-data segments.");
        }

        if (initView.Segment is not { } firstSegment)
        {
            throw new EbicsConnectorException(
                "The download initialisation reported success but carried no order-data segment.");
        }

        if (initView.EncryptedTransactionKey is not { } encryptedTransactionKey)
        {
            throw new EbicsConnectorException(
                "The download initialisation reported success but carried no encrypted transaction key.");
        }

        var segments = new List<byte[]>((int)numSegments) { firstSegment };

        // Transfer phase: one message per remaining segment (segment 1 arrived with the initialisation).
        for (var segmentNumber = 2UL; segmentNumber <= numSegments; segmentNumber++)
        {
            var transferEnvelope = builder.BuildTransferRequest(new DownloadTransferContext(
                connection.HostId.Value, transactionId, segmentNumber, segmentNumber == numSegments));
            var transferView = builder.ParseTransferResponse(await SignAndExchangeAsync(transferEnvelope).ConfigureAwait(false));
            if (transferView.ReturnCode != EbicsResult<DownloadResult>.OkReturnCode)
            {
                return EbicsResult<DownloadResult>.Failure(transferView.ReturnCode, transferView.ReportText);
            }

            if (transferView.Segment is not { } segment)
            {
                throw new EbicsConnectorException(
                    $"The download transfer response for segment {segmentNumber} carried no order data.");
            }

            segments.Add(segment);
        }

        // Reassemble → decrypt (subscriber private E002 key) → decompress. A failure here means the
        // received data is unusable: send a negative receipt so the server re-provides it, then throw.
        byte[] orderData;
        try
        {
            var ciphertext = EbicsSegmentation.Reassemble(segments);
            var compressed = EncryptionE002.Decrypt(new EncryptedOrderData(encryptedTransactionKey, ciphertext), encKey, encVersion);
            orderData = EbicsCompression.Decompress(compressed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await TrySendNegativeReceiptAsync().ConfigureAwait(false);
            throw new EbicsConnectorException(
                "The downloaded order data could not be decrypted or decompressed; a negative receipt was sent.", ex);
        }

        // Optional parsing hook (runs before the receipt, so a client-side post-processing failure also
        // yields a negative receipt).
        object? parsed = null;
        if (parse is not null)
        {
            try
            {
                parsed = parse(orderData);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await TrySendNegativeReceiptAsync().ConfigureAwait(false);
                throw;
            }
        }

        // Receipt phase: acknowledge the successful download (positive receipt).
        var receiptEnvelope = builder.BuildReceiptRequest(new DownloadReceiptContext(connection.HostId.Value, transactionId, ReceiptCodePositive));
        var receiptView = builder.ParseReceiptResponse(await SignAndExchangeAsync(receiptEnvelope).ConfigureAwait(false));

        return EbicsResult<DownloadResult>.Success(
            new DownloadResult
            {
                TransactionId = Convert.ToHexString(transactionId),
                NumSegments = (int)numSegments,
                OrderData = orderData,
                Parsed = parsed,
            },
            receiptView.ReturnCode,
            receiptView.ReportText);

        // --- local helpers (capture builder/connection/keys/ctx/ct) ------------------------------------

        async Task<string> SignAndExchangeAsync(IAuthSignedRequestEnvelope envelope)
        {
            Sign(envelope, authKey, authVersion);
            return await DownloadSupport.ExchangeAsync(envelope, ctx, ct).ConfigureAwait(false);
        }

        async Task TrySendNegativeReceiptAsync()
        {
            try
            {
                var negativeReceipt = builder.BuildReceiptRequest(
                    new DownloadReceiptContext(connection.HostId.Value, transactionId, ReceiptCodeNegative));
                await SignAndExchangeAsync(negativeReceipt).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best effort: the download already failed locally; a failing receipt must not mask that.
            }
        }
    }

    /// <summary>The receipt code acknowledging a successful download (post-processing done).</summary>
    private const byte ReceiptCodePositive = 0;

    /// <summary>The receipt code reporting a post-processing failure (the server re-provides the data).</summary>
    private const byte ReceiptCodeNegative = 1;

    private static void Sign(IAuthSignedRequestEnvelope envelope, RsaKeyMaterial authKey, KeyVersion authVersion)
    {
        var unsignedXml = EbicsXmlSerializer.SerializeToString(envelope);
        envelope.AuthSignature = AuthenticationSignature.Sign(unsignedXml, authKey, authVersion);
    }
}
