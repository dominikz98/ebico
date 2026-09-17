using EBICO.Core.Schema.XmlDsig;

namespace EBICO.Core.Versioning;

/// <summary>
/// An <see cref="IEbicsResponseEnvelope"/> that carries an EBICS authentication signature
/// (<c>AuthSignature</c>, key version X002) over its <c>authenticate="true"</c> nodes — the
/// transaction <c>ebicsResponse</c>. The counterpart of
/// <see cref="IAuthSignedRequestEnvelope"/> for the bank-to-subscriber direction.
/// </summary>
/// <remarks>
/// <para>
/// The <c>ebicsKeyManagementResponse</c> (INI/HIA/HPB) deliberately does <b>not</b> implement this
/// interface: its schema has no <c>AuthSignature</c> element, because the key-management responses
/// precede the key exchange that would make a signature verifiable. Real clients mirror that and
/// skip response verification for exactly those three orders.
/// </para>
/// <para>
/// Attached to the generated per-version envelope bindings by hand-written partial declarations (see
/// <c>Bindings/AuthSignedEnvelopeBindings.cs</c>), so the server can sign a response without knowing
/// its concrete version type.
/// </para>
/// </remarks>
public interface IAuthSignedResponseEnvelope : IEbicsResponseEnvelope
{
    /// <summary>The XML-DSig authentication signature over the response's authenticated node-set.</summary>
    SignatureType AuthSignature { get; set; }
}
