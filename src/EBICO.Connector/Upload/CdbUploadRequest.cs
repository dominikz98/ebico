using EBICO.Core.Payments;

namespace EBICO.Connector.Upload;

/// <summary>
/// Convenience upload request for a <b>SEPA Direct Debit (B2B)</b> (order type <c>CDB</c>, a
/// <c>pain.008</c> message). Equivalent to a <see cref="UploadRequest"/> with
/// <see cref="UploadRequest.OrderType"/> = <c>"CDB"</c>.
/// </summary>
public sealed class CdbUploadRequest : IEbicsRequest<UploadResult>, IPaymentUploadRequest
{
    /// <summary>The <c>pain.008</c> B2B direct-debit message to upload, as raw bytes.</summary>
    public ReadOnlyMemory<byte> Pain008 { get; init; }

    /// <summary>The maximum raw segment size in bytes, or <see langword="null"/> for the connector default.</summary>
    public int? MaxSegmentSizeBytes { get; init; }

    /// <summary>
    /// Asks the bank to park this payment for the <b>distributed electronic signature</b> (VEU/EDS)
    /// instead of executing it immediately (#124). See <see cref="UploadRequest.DistributedSignature"/>
    /// and <c>docs/connector/veu.md</c>.
    /// </summary>
    public bool DistributedSignature { get; init; }

    /// <inheritdoc />
    ReadOnlyMemory<byte> IPaymentUploadRequest.Payload => Pain008;

    /// <inheritdoc />
    string IPaymentUploadRequest.OrderType => PaymentOrderTypes.DirectDebitB2B;

    /// <inheritdoc />
    int? IPaymentUploadRequest.MaxSegmentSizeBytes => MaxSegmentSizeBytes;

    /// <inheritdoc />
    bool IPaymentUploadRequest.DistributedSignature => DistributedSignature;
}
