using EBICO.Core;
using EBICO.Core.Versioning;

namespace EBICO.Core.Schema.H005;

// Hand-written partial declarations that attach the version-independent envelope
// interfaces (and the ProtocolVersion discriminator) to the generated H005 bindings.
// See EnvelopeBindings.H003.cs for why these live outside Schema/{Hxxx}/.

/// <summary>H005 <c>ebicsRequest</c> envelope (client → server).</summary>
public partial class EbicsRequest : IEbicsRequestEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H005;
}

/// <summary>H005 <c>ebicsUnsecuredRequest</c> envelope (client → server).</summary>
public partial class EbicsUnsecuredRequest : IEbicsRequestEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H005;
}

/// <summary>H005 <c>ebicsUnsignedRequest</c> envelope (client → server).</summary>
public partial class EbicsUnsignedRequest : IEbicsRequestEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H005;
}

/// <summary>H005 <c>ebicsNoPubKeyDigestsRequest</c> envelope (client → server).</summary>
public partial class EbicsNoPubKeyDigestsRequest : IEbicsRequestEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H005;
}

/// <summary>H005 <c>ebicsResponse</c> envelope (server → client).</summary>
public partial class EbicsResponse : IEbicsResponseEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H005;
}

/// <summary>H005 <c>ebicsKeyManagementResponse</c> envelope (server → client).</summary>
public partial class EbicsKeyManagementResponse : IEbicsResponseEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H005;
}
