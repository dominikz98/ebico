using EBICO.Core.Btf;

namespace EBICO.Connector.Upload;

/// <summary>
/// A generic EBICS upload request: submits an order payload (e.g. a SEPA pain message) to the bank in
/// the two-phase upload transaction (initialisation → transfer). The connector compresses, E002-encrypts
/// and electronically signs the payload and segments the ciphertext.
/// </summary>
/// <remarks>
/// <para>
/// The order is identified in a version-appropriate way. On H005 the business transaction is submitted
/// as <c>BTU</c> with a <see cref="Btf"/> (resolved from <see cref="OrderType"/> when not supplied). On
/// H003/H004 either a classical order type (<see cref="OrderType"/>, e.g. <c>"CCT"</c>) is submitted
/// directly, or the generic <c>FUL</c> file upload is used when a <see cref="FileFormat"/> is given.
/// </para>
/// <para>
/// For the common SEPA payment orders prefer the convenience requests
/// (<see cref="CctUploadRequest"/>, <see cref="CddUploadRequest"/>, <see cref="CdbUploadRequest"/>,
/// <see cref="CipUploadRequest"/>). The subscriber must be fully onboarded (INI/HIA/HPB) so the bank's
/// E002 encryption key is available in the key store.
/// </para>
/// </remarks>
public sealed class UploadRequest : IEbicsRequest<UploadResult>
{
    /// <summary>The order payload to upload (e.g. a pain.001/pain.008 message), as raw bytes.</summary>
    public ReadOnlyMemory<byte> OrderData { get; init; }

    /// <summary>
    /// The order type: a classical code such as <c>"CCT"</c>/<c>"CDD"</c> (H003/H004 submit it directly;
    /// H005 resolves it to a BTF when <see cref="Btf"/> is not set), or <see langword="null"/> when a
    /// <see cref="Btf"/> (H005) or <see cref="FileFormat"/> (H003/H004 FUL) is supplied instead.
    /// </summary>
    public string? OrderType { get; init; }

    /// <summary>The H005 business transaction format placed in <c>BTUOrderParams/Service</c>; ignored for H003/H004.</summary>
    public BusinessTransactionFormat? Btf { get; init; }

    /// <summary>The H003/H004 <c>FULOrderParams/FileFormat</c> value for the generic file upload; ignored for H005.</summary>
    public string? FileFormat { get; init; }

    /// <summary>
    /// The maximum raw (pre-base64) segment size in bytes; larger payloads are split across several
    /// transfer messages. <see langword="null"/> uses the connector default.
    /// </summary>
    public int? MaxSegmentSizeBytes { get; init; }
}
