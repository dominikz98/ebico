namespace EBICO.Core;

/// <summary>
/// Identifies a supported EBICS protocol version. The values correspond to the
/// schema namespace prefixes used by the EBICS specifications.
/// </summary>
public enum EbicsVersion
{
    /// <summary>EBICS 2.4 (schema family <c>H003</c>).</summary>
    H003 = 3,

    /// <summary>EBICS 2.5 (schema family <c>H004</c>).</summary>
    H004 = 4,

    /// <summary>EBICS 3.0 (schema family <c>H005</c>).</summary>
    H005 = 5,
}
