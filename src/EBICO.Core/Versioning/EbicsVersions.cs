using System.Diagnostics.CodeAnalysis;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Core.Versioning;

/// <summary>
/// The single source of truth that maps an <see cref="EbicsVersion"/> to its
/// <see cref="EbicsVersionInfo"/> (schema code, root namespace URI and CLR envelope
/// types), with reverse lookups by namespace URI and by version code.
/// </summary>
/// <remarks>
/// This is the one place that knows the H003 legacy-namespace special case and the
/// version-to-type wiring. Server and connector code selects a target version through
/// here (e.g. <c>EbicsVersions.Get(options.Version).RequestType</c>).
/// </remarks>
public static class EbicsVersions
{
    private static readonly EbicsVersionInfo H003Info = new(
        EbicsVersion.H003,
        "H003",
        "http://www.ebics.org/H003",
        typeof(H003.EbicsRequest),
        typeof(H003.EbicsResponse),
        typeof(H003.EbicsUnsecuredRequest),
        typeof(H003.EbicsUnsignedRequest),
        typeof(H003.EbicsNoPubKeyDigestsRequest),
        typeof(H003.EbicsKeyManagementResponse));

    private static readonly EbicsVersionInfo H004Info = new(
        EbicsVersion.H004,
        "H004",
        "urn:org:ebics:H004",
        typeof(H004.EbicsRequest),
        typeof(H004.EbicsResponse),
        typeof(H004.EbicsUnsecuredRequest),
        typeof(H004.EbicsUnsignedRequest),
        typeof(H004.EbicsNoPubKeyDigestsRequest),
        typeof(H004.EbicsKeyManagementResponse));

    private static readonly EbicsVersionInfo H005Info = new(
        EbicsVersion.H005,
        "H005",
        "urn:org:ebics:H005",
        typeof(H005.EbicsRequest),
        typeof(H005.EbicsResponse),
        typeof(H005.EbicsUnsecuredRequest),
        typeof(H005.EbicsUnsignedRequest),
        typeof(H005.EbicsNoPubKeyDigestsRequest),
        typeof(H005.EbicsKeyManagementResponse));

    private static readonly EbicsVersionInfo[] AllInfos = [H003Info, H004Info, H005Info];

    /// <summary>All supported versions, ordered oldest (H003) to newest (H005).</summary>
    public static IReadOnlyList<EbicsVersionInfo> All => AllInfos;

    /// <summary>Returns the metadata for the given <paramref name="version"/>.</summary>
    /// <param name="version">The protocol version.</param>
    /// <returns>The matching <see cref="EbicsVersionInfo"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="version"/> is not a defined <see cref="EbicsVersion"/> value.
    /// </exception>
    public static EbicsVersionInfo Get(EbicsVersion version) => version switch
    {
        EbicsVersion.H003 => H003Info,
        EbicsVersion.H004 => H004Info,
        EbicsVersion.H005 => H005Info,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EBICS version."),
    };

    /// <summary>
    /// Resolves a protocol root namespace URI (e.g. <c>urn:org:ebics:H005</c> or the
    /// H003 legacy <c>http://www.ebics.org/H003</c>) to its version metadata.
    /// </summary>
    /// <param name="namespaceUri">The root element namespace URI to resolve.</param>
    /// <param name="info">The matching metadata when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the namespace belongs to a supported version.</returns>
    public static bool TryFromNamespace(string? namespaceUri, [NotNullWhen(true)] out EbicsVersionInfo? info)
    {
        foreach (var candidate in AllInfos)
        {
            if (string.Equals(candidate.NamespaceUri, namespaceUri, StringComparison.Ordinal))
            {
                info = candidate;
                return true;
            }
        }

        info = null;
        return false;
    }

    /// <summary>Resolves a four-character version code (e.g. <c>"H005"</c>) to its version metadata.</summary>
    /// <param name="code">The version code to resolve.</param>
    /// <param name="info">The matching metadata when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the code belongs to a supported version.</returns>
    public static bool TryFromCode(string? code, [NotNullWhen(true)] out EbicsVersionInfo? info)
    {
        foreach (var candidate in AllInfos)
        {
            if (string.Equals(candidate.Code, code, StringComparison.Ordinal))
            {
                info = candidate;
                return true;
            }
        }

        info = null;
        return false;
    }
}
