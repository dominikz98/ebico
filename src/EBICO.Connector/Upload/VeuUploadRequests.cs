using System.Text;
using EBICO.Core.Administrative;

namespace EBICO.Connector.Upload;

/// <summary>
/// Convenience upload request that <b>adds an electronic signature</b> to an order parked for the
/// distributed electronic signature (order type <c>HVE</c>). Equivalent to an
/// <see cref="UploadRequest"/> with <see cref="UploadRequest.OrderType"/> = <c>"HVE"</c> and
/// <see cref="UploadRequest.Veu"/> set.
/// </summary>
/// <remarks>
/// <para>
/// The order id comes from the <c>HVU</c>/<c>HVZ</c> overview. Once the bank has collected the required
/// number of signatures the order is released. Parking an order in the first place is the submitting
/// side's job — set <see cref="UploadRequest.DistributedSignature"/> on the original upload.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the EBICO emulator records <em>that</em> an authorised subscriber submitted
/// an HVE and does not verify the signature payload (ADR-0020). <see cref="SignaturePayload"/> therefore
/// carries a minimal placeholder by default. See <c>docs/connector/veu.md</c>.
/// </para>
/// </remarks>
public sealed class HveUploadRequest : IEbicsRequest<UploadResult>, IVeuUploadRequest
{
    private static readonly byte[] DefaultPayload = Encoding.UTF8.GetBytes("<HVEOrderData/>");

    /// <summary>The parked order to sign; its <see cref="VeuOrderReference.OrderId"/> comes from <c>HVU</c>/<c>HVZ</c>. Required.</summary>
    public required VeuOrderReference Order { get; init; }

    /// <summary>
    /// The signature order data sent as the payload. Defaults to a minimal placeholder element, which is
    /// what the emulator expects; a real bank requires the order's electronic signature here.
    /// </summary>
    public ReadOnlyMemory<byte> SignaturePayload { get; init; } = DefaultPayload;

    /// <summary>The maximum raw segment size in bytes, or <see langword="null"/> for the connector default.</summary>
    public int? MaxSegmentSizeBytes { get; init; }

    /// <inheritdoc />
    ReadOnlyMemory<byte> IVeuUploadRequest.Payload => SignaturePayload.IsEmpty ? DefaultPayload : SignaturePayload;

    /// <inheritdoc />
    string IVeuUploadRequest.OrderType => VeuOrderTypes.AddSignature;

    /// <inheritdoc />
    VeuOrderReference IVeuUploadRequest.Order => Order;

    /// <inheritdoc />
    int? IVeuUploadRequest.MaxSegmentSizeBytes => MaxSegmentSizeBytes;
}

/// <summary>
/// Convenience upload request that <b>cancels/rejects</b> an order parked for the distributed electronic
/// signature (order type <c>HVS</c>). Equivalent to an <see cref="UploadRequest"/> with
/// <see cref="UploadRequest.OrderType"/> = <c>"HVS"</c> and <see cref="UploadRequest.Veu"/> set.
/// </summary>
/// <remarks>
/// Allowed for the submitting subscriber and for anyone authorised to sign the order; the parked order
/// is removed without being executed. See <c>docs/connector/veu.md</c>.
/// </remarks>
public sealed class HvsUploadRequest : IEbicsRequest<UploadResult>, IVeuUploadRequest
{
    private static readonly byte[] DefaultPayload = Encoding.UTF8.GetBytes("<HVSOrderData/>");

    /// <summary>The parked order to cancel; its <see cref="VeuOrderReference.OrderId"/> comes from <c>HVU</c>/<c>HVZ</c>. Required.</summary>
    public required VeuOrderReference Order { get; init; }

    /// <summary>
    /// The cancellation order data sent as the payload. Defaults to a minimal placeholder element, which
    /// is what the emulator expects.
    /// </summary>
    public ReadOnlyMemory<byte> CancellationPayload { get; init; } = DefaultPayload;

    /// <summary>The maximum raw segment size in bytes, or <see langword="null"/> for the connector default.</summary>
    public int? MaxSegmentSizeBytes { get; init; }

    /// <inheritdoc />
    ReadOnlyMemory<byte> IVeuUploadRequest.Payload => CancellationPayload.IsEmpty ? DefaultPayload : CancellationPayload;

    /// <inheritdoc />
    string IVeuUploadRequest.OrderType => VeuOrderTypes.CancelOrder;

    /// <inheritdoc />
    VeuOrderReference IVeuUploadRequest.Order => Order;

    /// <inheritdoc />
    int? IVeuUploadRequest.MaxSegmentSizeBytes => MaxSegmentSizeBytes;
}
