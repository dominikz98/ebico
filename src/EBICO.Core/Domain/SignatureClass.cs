namespace EBICO.Core.Domain;

/// <summary>
/// The signature class of a subscriber's authorisation for an order. Mirrors the schema
/// type <c>AuthorisationLevelType</c> (identical across H003/H004/H005) and captures the
/// core distinction between a transport signature and a bank-technical (authorising) one.
/// </summary>
/// <remarks>
/// Use <see cref="SignatureClassExtensions.IsTransportOnly(SignatureClass)"/> and
/// <see cref="SignatureClassExtensions.IsBankTechnical(SignatureClass)"/> to classify a value.
/// </remarks>
public enum SignatureClass
{
    /// <summary>Sole signature (<c>E</c>, Einzelunterschrift): authorises an order on its own.</summary>
    E,

    /// <summary>First signature (<c>A</c>, Erstunterschrift): a bank-technical signature that needs a second one.</summary>
    A,

    /// <summary>Second signature (<c>B</c>, Zweitunterschrift): a bank-technical signature complementing a first one.</summary>
    B,

    /// <summary>Transport signature (<c>T</c>, Transportunterschrift): submits order data without a bank-technical (authorising) signature.</summary>
    T,
}

/// <summary>
/// Classification helpers for <see cref="SignatureClass"/> that express the EBICS
/// distinction between transport and bank-technical signatures.
/// </summary>
public static class SignatureClassExtensions
{
    /// <summary>
    /// Indicates whether <paramref name="signatureClass"/> is the transport class
    /// (<see cref="SignatureClass.T"/>) — order data is submitted without contributing a
    /// bank-technical (authorising) signature.
    /// </summary>
    /// <param name="signatureClass">The signature class to classify.</param>
    /// <returns><see langword="true"/> for <see cref="SignatureClass.T"/>.</returns>
    public static bool IsTransportOnly(this SignatureClass signatureClass)
        => signatureClass == SignatureClass.T;

    /// <summary>
    /// Indicates whether <paramref name="signatureClass"/> is a bank-technical
    /// (authorising) signature — <see cref="SignatureClass.E"/>,
    /// <see cref="SignatureClass.A"/> or <see cref="SignatureClass.B"/>.
    /// </summary>
    /// <param name="signatureClass">The signature class to classify.</param>
    /// <returns><see langword="true"/> for <c>E</c>, <c>A</c> or <c>B</c>.</returns>
    public static bool IsBankTechnical(this SignatureClass signatureClass)
        => signatureClass is SignatureClass.E or SignatureClass.A or SignatureClass.B;
}
