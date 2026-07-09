using EBICO.Core;
using EBICO.Core.Versioning;

namespace EBICO.Server.Pipeline;

/// <summary>
/// Carries the data of a single inbound EBICS request through the pipeline stages: the raw XML,
/// the detected version, the parsed request envelope and the extracted order type.
/// </summary>
public sealed class EbicsRequestContext
{
    /// <summary>Initializes a new <see cref="EbicsRequestContext"/>.</summary>
    /// <param name="requestXml">The raw request XML.</param>
    /// <param name="versionInfo">The detected EBICS version information.</param>
    /// <param name="envelope">The parsed request envelope.</param>
    /// <param name="orderType">The extracted order type, or <see langword="null"/> when absent/not applicable.</param>
    public EbicsRequestContext(
        string requestXml,
        EbicsVersionInfo versionInfo,
        IEbicsRequestEnvelope envelope,
        string? orderType)
    {
        ArgumentNullException.ThrowIfNull(requestXml);
        ArgumentNullException.ThrowIfNull(versionInfo);
        ArgumentNullException.ThrowIfNull(envelope);

        RequestXml = requestXml;
        VersionInfo = versionInfo;
        Envelope = envelope;
        OrderType = orderType;
    }

    /// <summary>The raw request XML as received.</summary>
    public string RequestXml { get; }

    /// <summary>The detected EBICS version information.</summary>
    public EbicsVersionInfo VersionInfo { get; }

    /// <summary>The detected EBICS protocol version.</summary>
    public EbicsVersion Version => VersionInfo.Version;

    /// <summary>The parsed request envelope.</summary>
    public IEbicsRequestEnvelope Envelope { get; }

    /// <summary>The extracted order type (e.g. <c>"HPB"</c>), or <see langword="null"/> when absent.</summary>
    public string? OrderType { get; }
}
