using EBICO.Core.Schema.XmlDsig;

namespace EBICO.Core.Versioning;

/// <summary>
/// An <see cref="IEbicsRequestEnvelope"/> that carries an EBICS authentication signature
/// (<c>AuthSignature</c>, key version X002) over its <c>authenticate="true"</c> nodes — currently
/// the <c>ebicsNoPubKeyDigestsRequest</c> used by the HPB onboarding flow, and later the transaction
/// <c>ebicsRequest</c>.
/// </summary>
/// <remarks>
/// Attached to the generated per-version envelope bindings by hand-written partial declarations (see
/// <c>Bindings/AuthSignedEnvelopeBindings.cs</c>), so a handler can set the computed signature on the
/// envelope without knowing its concrete version type.
/// </remarks>
public interface IAuthSignedRequestEnvelope : IEbicsRequestEnvelope
{
    /// <summary>The XML-DSig authentication signature over the request's authenticated node-set.</summary>
    SignatureType AuthSignature { get; set; }
}
