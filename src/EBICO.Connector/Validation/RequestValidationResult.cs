using EBICO.Core.Btf;
using EBICO.Core.ReturnCodes;

namespace EBICO.Connector.Validation;

/// <summary>
/// The resolved, version-appropriate order identity of a validated <b>upload</b> request: the header
/// order type actually put on the wire (<c>BTU</c>/<c>FUL</c> or a classical code), the optional H005
/// business transaction format and H003/H004 file format, and the <see cref="EffectiveOrderType"/> — the
/// classical order-type code used as the authorisation key.
/// </summary>
/// <param name="HeaderOrderType">The order type placed in the request header (e.g. <c>"BTU"</c>, <c>"FUL"</c>, <c>"CCT"</c>).</param>
/// <param name="Btf">The H005 business transaction format, or <see langword="null"/> for H003/H004.</param>
/// <param name="FileFormat">The H003/H004 <c>FULOrderParams/FileFormat</c>, or <see langword="null"/>.</param>
/// <param name="EffectiveOrderType">The effective classical order-type code (the authorisation key), or <see langword="null"/> when none could be resolved.</param>
internal readonly record struct ValidatedUploadIdentity(
    string HeaderOrderType,
    BusinessTransactionFormat? Btf,
    string? FileFormat,
    string? EffectiveOrderType);

/// <summary>
/// The resolved, version-appropriate order identity of a validated <b>download</b> request: the header
/// order type actually put on the wire (<c>BTD</c>/<c>FDL</c>, a classical code or an administrative
/// order type), the optional H005 business transaction format and H003/H004 file format, and the
/// <see cref="EffectiveOrderType"/> — the classical order-type code used as the authorisation key.
/// </summary>
/// <param name="HeaderOrderType">The order type placed in the request header (e.g. <c>"BTD"</c>, <c>"FDL"</c>, <c>"STA"</c>, <c>"HTD"</c>).</param>
/// <param name="Btf">The H005 business transaction format, or <see langword="null"/> for H003/H004 and administrative orders.</param>
/// <param name="FileFormat">The H003/H004 <c>FDLOrderParams/FileFormat</c>, or <see langword="null"/>.</param>
/// <param name="EffectiveOrderType">The effective classical order-type code (the authorisation key), or <see langword="null"/> when none could be resolved.</param>
internal readonly record struct ValidatedDownloadIdentity(
    string HeaderOrderType,
    BusinessTransactionFormat? Btf,
    string? FileFormat,
    string? EffectiveOrderType);

/// <summary>
/// The outcome of the client-side send validation (send-pipeline stage 1). Structural/BTF problems are
/// raised as an <see cref="EbicsConfigurationException"/> by the validator directly (a programming/config
/// error); this type only distinguishes an <see cref="Authorized"/> request (carrying its resolved
/// <see cref="Identity"/>) from a <see cref="Denied"/> one (carrying the return code the executor turns
/// into an <see cref="EbicsResult{T}"/> failure, mirroring what the bank would report).
/// </summary>
/// <typeparam name="TIdentity">The resolved identity type (<see cref="ValidatedUploadIdentity"/> or <see cref="ValidatedDownloadIdentity"/>).</typeparam>
internal readonly record struct RequestValidation<TIdentity>
{
    private RequestValidation(bool isAuthorized, TIdentity identity, string returnCode, string? returnText)
    {
        IsAuthorized = isAuthorized;
        Identity = identity;
        ReturnCode = returnCode;
        ReturnText = returnText;
    }

    /// <summary>Whether the request passed the (opt-in) client-side authorisation check.</summary>
    public bool IsAuthorized { get; }

    /// <summary>The resolved order identity; valid only when <see cref="IsAuthorized"/> is <see langword="true"/>.</summary>
    public TIdentity Identity { get; }

    /// <summary>The EBICS return code when the request was denied (<c>090003</c>); <see cref="EbicsReturnCode.OkCode"/> otherwise.</summary>
    public string ReturnCode { get; }

    /// <summary>The human-readable report text when the request was denied, or <see langword="null"/>.</summary>
    public string? ReturnText { get; }

    /// <summary>Creates an authorised outcome carrying the resolved <paramref name="identity"/>.</summary>
    /// <param name="identity">The resolved order identity.</param>
    /// <returns>An authorised validation outcome.</returns>
    public static RequestValidation<TIdentity> Authorized(TIdentity identity)
        => new(isAuthorized: true, identity, EbicsReturnCode.OkCode, returnText: null);

    /// <summary>Creates a denied outcome carrying the return code and text to report to the caller.</summary>
    /// <param name="returnCode">The EBICS return code (e.g. <c>"090003"</c>).</param>
    /// <param name="returnText">The human-readable report text, or <see langword="null"/>.</param>
    /// <returns>A denied validation outcome.</returns>
    public static RequestValidation<TIdentity> Denied(string returnCode, string? returnText)
        => new(isAuthorized: false, identity: default!, returnCode, returnText);
}
