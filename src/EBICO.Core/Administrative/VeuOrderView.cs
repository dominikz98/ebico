using EBICO.Core.Domain;

namespace EBICO.Core.Administrative;

/// <summary>
/// A version-neutral projection of one order awaiting distributed signatures (VEU / EDS, issue #42), the
/// input the <see cref="VeuResponseBuilder"/> renders into the per-version HVU/HVZ/HVD/HVT response bindings.
/// The server builds these from its open-order store; the builder maps them onto the generated schema types.
/// </summary>
/// <param name="OrderId">The server-assigned order identifier (4 chars, pattern <c>[A-Z][A-Z0-9]{3}</c>).</param>
/// <param name="OrderType">The effective classical order-type code of the underlying order (e.g. <c>"CCT"</c>).</param>
/// <param name="OrderDataSize">The size in bytes of the (uncompressed) order data.</param>
/// <param name="NumSigRequired">The number of bank-technical signatures the order requires before release.</param>
/// <param name="NumSigDone">The number of bank-technical signatures already applied.</param>
/// <param name="ReadyToBeSigned">Whether the order can still receive a further signature.</param>
/// <param name="Originator">The subscriber that submitted the order into the EDS.</param>
/// <param name="Signers">The subscribers that have already signed the order (may be empty).</param>
/// <param name="DataDigest">The digest (hash) over the order data, surfaced by HVD/HVZ.</param>
/// <param name="AdditionalOrderInfo">Optional free-text additional information about the order.</param>
public sealed record VeuOrderView(
    string OrderId,
    string OrderType,
    int OrderDataSize,
    int NumSigRequired,
    int NumSigDone,
    bool ReadyToBeSigned,
    VeuSignerView Originator,
    IReadOnlyList<VeuSignerView> Signers,
    byte[] DataDigest,
    string? AdditionalOrderInfo = null);

/// <summary>
/// A version-neutral projection of a party involved in an EDS order (issue #42): either the originator that
/// submitted it or a user that has already signed it.
/// </summary>
/// <param name="PartnerId">The partner (customer) identifier.</param>
/// <param name="UserId">The user (subscriber) identifier.</param>
/// <param name="Name">The optional human-readable name of the user.</param>
/// <param name="Timestamp">When the party submitted or signed the order.</param>
/// <param name="Permission">The signature class the signer used, or <see langword="null"/> for the originator.</param>
public sealed record VeuSignerView(
    string PartnerId,
    string UserId,
    string? Name,
    DateTimeOffset Timestamp,
    SignatureClass? Permission);
