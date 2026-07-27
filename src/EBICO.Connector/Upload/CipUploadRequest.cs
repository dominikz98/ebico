using EBICO.Core.Payments;

namespace EBICO.Connector.Upload;

/// <summary>
/// Convenience upload request for a <b>SEPA Instant Credit Transfer</b> (order type <c>CIP</c>, a
/// <c>pain.001</c> message). Equivalent to a <see cref="UploadRequest"/> with
/// <see cref="UploadRequest.OrderType"/> = <c>"CIP"</c>.
/// </summary>
public sealed class CipUploadRequest : IEbicsRequest<UploadResult>, IPaymentUploadRequest
{
    /// <summary>The <c>pain.001</c> instant credit-transfer message to upload, as raw bytes.</summary>
    public ReadOnlyMemory<byte> Pain001 { get; init; }

    /// <summary>The maximum raw segment size in bytes, or <see langword="null"/> for the connector default.</summary>
    public int? MaxSegmentSizeBytes { get; init; }

    /// <summary>
    /// Asks the bank to park this payment for the <b>distributed electronic signature</b> (VEU/EDS)
    /// instead of executing it immediately (#124). See <see cref="UploadRequest.DistributedSignature"/>
    /// and <c>docs/connector/veu.md</c>.
    /// </summary>
    public bool DistributedSignature { get; init; }

    /// <inheritdoc />
    ReadOnlyMemory<byte> IPaymentUploadRequest.Payload => Pain001;

    /// <inheritdoc />
    string IPaymentUploadRequest.OrderType => PaymentOrderTypes.InstantCreditTransfer;

    /// <inheritdoc />
    int? IPaymentUploadRequest.MaxSegmentSizeBytes => MaxSegmentSizeBytes;

    /// <inheritdoc />
    bool IPaymentUploadRequest.DistributedSignature => DistributedSignature;
}
