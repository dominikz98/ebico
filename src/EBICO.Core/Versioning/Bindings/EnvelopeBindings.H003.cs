using EBICO.Core;
using EBICO.Core.Versioning;

namespace EBICO.Core.Schema.H003;

// Hand-written partial declarations that attach the version-independent envelope
// interfaces (and the ProtocolVersion discriminator) to the generated H003 bindings.
//
// These live under Versioning/Bindings/ rather than next to the generated files in
// Schema/H003/ on purpose: scripts/generate-bindings.sh deletes and recreates the
// Schema/{Hxxx}/ directories on every run (rm -rf), which would wipe hand-written
// files placed there. The C# namespace stays EBICO.Core.Schema.H003 regardless of
// folder, so the partials still merge with the generated classes. The generated
// partials already provide Version/Revision (via IVersionAttrGroup); only
// ProtocolVersion is added here. See docs/protocol/version-dispatch.md.

/// <summary>H003 <c>ebicsRequest</c> envelope (client → server).</summary>
public partial class EbicsRequest : IEbicsRequestEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H003;
}

/// <summary>H003 <c>ebicsUnsecuredRequest</c> envelope (client → server).</summary>
public partial class EbicsUnsecuredRequest : IEbicsRequestEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H003;
}

/// <summary>H003 <c>ebicsUnsignedRequest</c> envelope (client → server).</summary>
public partial class EbicsUnsignedRequest : IEbicsRequestEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H003;
}

/// <summary>H003 <c>ebicsNoPubKeyDigestsRequest</c> envelope (client → server).</summary>
public partial class EbicsNoPubKeyDigestsRequest : IEbicsRequestEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H003;
}

/// <summary>H003 <c>ebicsResponse</c> envelope (server → client).</summary>
public partial class EbicsResponse : IEbicsResponseEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H003;
}

/// <summary>H003 <c>ebicsKeyManagementResponse</c> envelope (server → client).</summary>
public partial class EbicsKeyManagementResponse : IEbicsResponseEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H003;
}
