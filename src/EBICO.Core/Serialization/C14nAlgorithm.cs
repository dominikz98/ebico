namespace EBICO.Core.Serialization;

/// <summary>
/// The XML canonicalization variant to apply. EBICS authentication signatures rely on
/// W3C XML Canonicalization; this enum distinguishes the inclusive (<em>Canonical XML 1.0</em>)
/// from the exclusive (<em>Exclusive XML Canonicalization 1.0</em>) family and whether
/// comments are retained.
/// </summary>
/// <remarks>
/// The default used throughout EBICO is <see cref="Inclusive"/> (Canonical XML 1.0 without
/// comments): it is the variant EBICS authentication signatures most likely require. The
/// exact algorithm must still be verified against the official EBICS Annex once the schemas
/// are available — see <c>docs/protocol/serialization-c14n.md</c>.
/// </remarks>
public enum C14nMode
{
    /// <summary>Canonical XML 1.0, comments removed (<see cref="C14nAlgorithms.Inclusive"/>).</summary>
    Inclusive,

    /// <summary>Canonical XML 1.0, comments retained (<see cref="C14nAlgorithms.InclusiveWithComments"/>).</summary>
    InclusiveWithComments,

    /// <summary>Exclusive XML Canonicalization 1.0, comments removed (<see cref="C14nAlgorithms.Exclusive"/>).</summary>
    Exclusive,

    /// <summary>Exclusive XML Canonicalization 1.0, comments retained (<see cref="C14nAlgorithms.ExclusiveWithComments"/>).</summary>
    ExclusiveWithComments,
}

/// <summary>
/// The standard W3C algorithm-identifier URIs for the four canonicalization variants, plus
/// conversions to and from <see cref="C14nMode"/>. These URIs are exactly the values that
/// appear in a <c>ds:CanonicalizationMethod/@Algorithm</c> attribute
/// (<see cref="Schema.XmlDsig.CanonicalizationMethodType"/>), so signature code can map a
/// <c>SignedInfo</c> to the mode to apply and back.
/// </summary>
public static class C14nAlgorithms
{
    /// <summary>Canonical XML 1.0 without comments: <c>http://www.w3.org/TR/2001/REC-xml-c14n-20010315</c>.</summary>
    public const string Inclusive = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";

    /// <summary>Canonical XML 1.0 with comments: <c>http://www.w3.org/TR/2001/REC-xml-c14n-20010315#WithComments</c>.</summary>
    public const string InclusiveWithComments = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315#WithComments";

    /// <summary>Exclusive XML Canonicalization 1.0 without comments: <c>http://www.w3.org/2001/10/xml-exc-c14n#</c>.</summary>
    public const string Exclusive = "http://www.w3.org/2001/10/xml-exc-c14n#";

    /// <summary>Exclusive XML Canonicalization 1.0 with comments: <c>http://www.w3.org/2001/10/xml-exc-c14n#WithComments</c>.</summary>
    public const string ExclusiveWithComments = "http://www.w3.org/2001/10/xml-exc-c14n#WithComments";

    /// <summary>Returns the W3C algorithm URI for <paramref name="mode"/>.</summary>
    /// <param name="mode">The canonicalization variant.</param>
    /// <returns>The matching algorithm-identifier URI.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not a defined <see cref="C14nMode"/> value.
    /// </exception>
    public static string ToAlgorithmUri(C14nMode mode) => mode switch
    {
        C14nMode.Inclusive => Inclusive,
        C14nMode.InclusiveWithComments => InclusiveWithComments,
        C14nMode.Exclusive => Exclusive,
        C14nMode.ExclusiveWithComments => ExclusiveWithComments,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown canonicalization mode."),
    };

    /// <summary>
    /// Resolves a <c>ds:CanonicalizationMethod/@Algorithm</c> URI to its <see cref="C14nMode"/>.
    /// </summary>
    /// <param name="algorithmUri">The algorithm-identifier URI. Compared ordinally (case-sensitive).</param>
    /// <returns>The matching canonicalization variant.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="algorithmUri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="algorithmUri"/> is not one of the four supported canonicalization URIs.
    /// </exception>
    public static C14nMode FromAlgorithmUri(string algorithmUri)
    {
        ArgumentNullException.ThrowIfNull(algorithmUri);

        return algorithmUri switch
        {
            Inclusive => C14nMode.Inclusive,
            InclusiveWithComments => C14nMode.InclusiveWithComments,
            Exclusive => C14nMode.Exclusive,
            ExclusiveWithComments => C14nMode.ExclusiveWithComments,
            _ => throw new ArgumentException(
                $"Unsupported canonicalization algorithm '{algorithmUri}'.", nameof(algorithmUri)),
        };
    }
}
