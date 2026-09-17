using EBICO.Core.Versioning;

// Hand-written partial declarations that attach the version-independent
// IAuthSignedRequestEnvelope / IAuthSignedResponseEnvelope interfaces to the generated
// ebicsNoPubKeyDigestsRequest, ebicsRequest and ebicsResponse bindings. The generated classes already
// expose a `SignatureType AuthSignature { get; set; }` property, so the interfaces are satisfied
// implicitly — this only declares them so a handler can set the signature version-agnostically.
// See EnvelopeBindings.H00x.cs for the same partial pattern.
//
// ebicsKeyManagementResponse is deliberately absent: its schema carries no AuthSignature element
// (the key-management responses precede the key exchange), so there is nothing to attach.

namespace EBICO.Core.Schema.H003
{
    /// <summary>H003 <c>ebicsNoPubKeyDigestsRequest</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsNoPubKeyDigestsRequest : IAuthSignedRequestEnvelope
    {
    }

    /// <summary>H003 transaction <c>ebicsRequest</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsRequest : IAuthSignedRequestEnvelope
    {
    }

    /// <summary>H003 transaction <c>ebicsResponse</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsResponse : IAuthSignedResponseEnvelope
    {
    }
}

namespace EBICO.Core.Schema.H004
{
    /// <summary>H004 <c>ebicsNoPubKeyDigestsRequest</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsNoPubKeyDigestsRequest : IAuthSignedRequestEnvelope
    {
    }

    /// <summary>H004 transaction <c>ebicsRequest</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsRequest : IAuthSignedRequestEnvelope
    {
    }

    /// <summary>H004 transaction <c>ebicsResponse</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsResponse : IAuthSignedResponseEnvelope
    {
    }
}

namespace EBICO.Core.Schema.H005
{
    /// <summary>H005 <c>ebicsNoPubKeyDigestsRequest</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsNoPubKeyDigestsRequest : IAuthSignedRequestEnvelope
    {
    }

    /// <summary>H005 transaction <c>ebicsRequest</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsRequest : IAuthSignedRequestEnvelope
    {
    }

    /// <summary>H005 transaction <c>ebicsResponse</c> — carries the X002 <c>AuthSignature</c>.</summary>
    public partial class EbicsResponse : IAuthSignedResponseEnvelope
    {
    }
}
