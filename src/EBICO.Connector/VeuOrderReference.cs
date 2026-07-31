using EBICO.Core.Btf;

namespace EBICO.Connector;

/// <summary>
/// References an order held in the bank's distributed-electronic-signature (VEU/EDS) store. Required by
/// the VEU orders that act on one specific parked order — <c>HVE</c> (add a signature) and <c>HVS</c>
/// (cancel) on the upload side, <c>HVD</c> (status) and <c>HVT</c> (transaction details) on the download
/// side. The overview orders <c>HVU</c>/<c>HVZ</c> list all open orders and need no reference.
/// </summary>
/// <remarks>
/// <para>
/// The order id is assigned by the bank when the order is parked and is reported back by
/// <c>HVU</c>/<c>HVZ</c>. Besides the id, the EBICS order params identify the <em>referenced</em> order:
/// the submitting customer (<see cref="PartnerId"/>) and the order's own identity — its classical order
/// type on H003/H004 (<see cref="OrderType"/>, plus <see cref="FileFormat"/> for an <c>FUL</c>
/// submission) or its BTF service on H005 (<see cref="Btf"/>). Everything except
/// <see cref="OrderId"/> is optional: <see cref="PartnerId"/> falls back to the connection's own
/// <c>PartnerID</c>, and the identity fields are resolved from <see cref="OrderType"/> where possible.
/// </para>
/// <para>
/// <b>⚠️ Spec caveat:</b> the EBICO emulator keys its VEU store on the order id alone and ignores the
/// remaining fields; they are emitted for conformance with the published order-params schema and are not
/// verified against a real bank (#124, see <c>docs/connector/veu.md</c>).
/// </para>
/// </remarks>
public sealed class VeuOrderReference
{
    /// <summary>
    /// The bank-assigned order id of the parked order (EBICS <c>OrderID</c>), as reported by
    /// <c>HVU</c>/<c>HVZ</c>. Required.
    /// </summary>
    public required string OrderId { get; init; }

    /// <summary>
    /// The <c>PartnerID</c> of the customer that submitted the referenced order. When
    /// <see langword="null"/> the connection's own <c>PartnerID</c> is used (the common case: signing an
    /// order of one's own customer).
    /// </summary>
    public string? PartnerId { get; init; }

    /// <summary>
    /// The classical order type of the referenced order (e.g. <c>"CCT"</c>). Emitted in the H003/H004
    /// order params; on H005 it is used to resolve <see cref="Btf"/> when that is not supplied.
    /// </summary>
    public string? OrderType { get; init; }

    /// <summary>
    /// The H005 BTF service of the referenced order, placed in the order params' <c>Service</c> element.
    /// When <see langword="null"/> it is resolved from <see cref="OrderType"/>; ignored on H003/H004.
    /// </summary>
    public BusinessTransactionFormat? Btf { get; init; }

    /// <summary>
    /// The <c>FileFormat</c> of the referenced order when it was submitted as a generic H004 <c>FUL</c>
    /// upload. Ignored on H003 and H005.
    /// </summary>
    public string? FileFormat { get; init; }
}
