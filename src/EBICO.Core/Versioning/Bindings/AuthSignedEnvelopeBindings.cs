using EBICO.Core.Versioning;

// Hand-written partial declarations that attach the version-independent
// IAuthSignedRequestEnvelope interface to the generated ebicsNoPubKeyDigestsRequest bindings.
// The generated classes already expose a `SignatureType AuthSignature { get; set; }` property, so
// the interface is satisfied implicitly — this only declares the interface so a handler can set the
// signature version-agnostically. See EnvelopeBindings.H00x.cs for the same partial pattern.

namespace EBICO.Core.Schema.H003
{
    /// <summary>H003 <c>ebicsNoPubKeyDigestsRequest</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsNoPubKeyDigestsRequest : IAuthSignedRequestEnvelope
    {
    }
}

namespace EBICO.Core.Schema.H004
{
    /// <summary>H004 <c>ebicsNoPubKeyDigestsRequest</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsNoPubKeyDigestsRequest : IAuthSignedRequestEnvelope
    {
    }
}

namespace EBICO.Core.Schema.H005
{
    /// <summary>H005 <c>ebicsNoPubKeyDigestsRequest</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsNoPubKeyDigestsRequest : IAuthSignedRequestEnvelope
    {
    }
}
