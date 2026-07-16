using EBICO.Core.Payments;

namespace EBICO.Connector.Upload;

/// <summary>
/// Convenience upload request for a <b>SEPA Direct Debit (CORE)</b> (order type <c>CDD</c>, a
/// <c>pain.008</c> message). Equivalent to a <see cref="UploadRequest"/> with
/// <see cref="UploadRequest.OrderType"/> = <c>"CDD"</c>.
/// </summary>
public sealed class CddUploadRequest : IEbicsRequest<UploadResult>, IPaymentUploadRequest
{
    /// <summary>The <c>pain.008</c> direct-debit message to upload, as raw bytes.</summary>
    public ReadOnlyMemory<byte> Pain008 { get; init; }

    /// <summary>The maximum raw segment size in bytes, or <see langword="null"/> for the connector default.</summary>
    public int? MaxSegmentSizeBytes { get; init; }

    /// <inheritdoc />
    ReadOnlyMemory<byte> IPaymentUploadRequest.Payload => Pain008;

    /// <inheritdoc />
    string IPaymentUploadRequest.OrderType => PaymentOrderTypes.DirectDebitCore;

    /// <inheritdoc />
    int? IPaymentUploadRequest.MaxSegmentSizeBytes => MaxSegmentSizeBytes;
}
