using System.Security.Cryptography;
using EBICO.Core;
using EBICO.Core.Administrative;
using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>
/// An order held in the distributed-signature processing unit (EDS / "verteilte elektronische Unterschrift",
/// VEU — issue #42): an uploaded order that was submitted for distributed signing and is waiting for further
/// bank-technical signatures before it can be released. Unlike the transient upload/download transactions
/// (issues #32/#33), an open VEU order lives — partner-scoped — until it is fully signed (released) or
/// cancelled (HVS), so it is a mutable aggregate that accumulates signers over time.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the electronic signatures are not verified; a "signature" here is the fact that
/// an authorised subscriber submitted an HVE for the order. Whether an upload needs distributed signing, and
/// how many signatures it requires, is approximated (the submission carries a distributed-signature flag and
/// the server applies a fixed required-signature count); the real bank-side account signature rules are out
/// of scope. See <c>docs/server/veu-orders.md</c> and ADR-0020.
/// </remarks>
public sealed class OpenVeuOrder
{
    private readonly List<VeuSignerView> _signers;

    /// <summary>Creates an open VEU order. The <see cref="OrderId"/> is assigned by the store on add.</summary>
    /// <param name="hostId">The bank/host the order belongs to.</param>
    /// <param name="partnerId">The partner (customer) the order belongs to.</param>
    /// <param name="version">The protocol version the order was submitted under.</param>
    /// <param name="effectiveOrderType">The resolved classical order-type code of the underlying order (e.g. <c>"CCT"</c>).</param>
    /// <param name="orderData">The plaintext order data awaiting signatures.</param>
    /// <param name="originator">The subscriber that submitted the order into the EDS.</param>
    /// <param name="numSigRequired">The number of bank-technical signatures required before release.</param>
    /// <param name="createdAt">When the order was parked.</param>
    /// <param name="initialSigners">The signatures already present at submission (e.g. the submitter's own), or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public OpenVeuOrder(
        HostId hostId,
        PartnerId partnerId,
        EbicsVersion version,
        string effectiveOrderType,
        byte[] orderData,
        VeuSignerView originator,
        int numSigRequired,
        DateTimeOffset createdAt,
        IEnumerable<VeuSignerView>? initialSigners = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveOrderType);
        ArgumentNullException.ThrowIfNull(orderData);
        ArgumentNullException.ThrowIfNull(originator);

        HostId = hostId;
        PartnerId = partnerId;
        Version = version;
        EffectiveOrderType = effectiveOrderType;
        OrderData = orderData;
        Originator = originator;
        NumSigRequired = numSigRequired;
        CreatedAt = createdAt;
        _signers = initialSigners is null ? [] : [.. initialSigners];
    }

    /// <summary>The server-assigned order identifier (4 chars, pattern <c>[A-Z][A-Z0-9]{3}</c>); assigned on add.</summary>
    public string OrderId { get; internal set; } = string.Empty;

    /// <summary>The bank/host the order belongs to.</summary>
    public HostId HostId { get; }

    /// <summary>The partner (customer) the order belongs to.</summary>
    public PartnerId PartnerId { get; }

    /// <summary>The protocol version the order was submitted under.</summary>
    public EbicsVersion Version { get; }

    /// <summary>The resolved classical order-type code of the underlying order (e.g. <c>"CCT"</c>).</summary>
    public string EffectiveOrderType { get; }

    /// <summary>The plaintext order data awaiting signatures.</summary>
    public byte[] OrderData { get; }

    /// <summary>The subscriber that submitted the order into the EDS.</summary>
    public VeuSignerView Originator { get; }

    /// <summary>The number of bank-technical signatures required before the order can be released.</summary>
    public int NumSigRequired { get; }

    /// <summary>When the order was parked.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>The subscribers that have already signed the order.</summary>
    public IReadOnlyList<VeuSignerView> Signers => _signers;

    /// <summary>The number of bank-technical signatures already applied.</summary>
    public int NumSigDone => _signers.Count;

    /// <summary>Whether the order has reached the number of signatures required for release.</summary>
    public bool IsFullySigned => NumSigDone >= NumSigRequired;

    /// <summary>Whether the order can still receive a further signature (it is not yet fully signed).</summary>
    public bool ReadyToBeSigned => !IsFullySigned;

    /// <summary>Whether the given user has already signed this order.</summary>
    /// <param name="userId">The user to check.</param>
    /// <returns><see langword="true"/> when a signature from the user is already present.</returns>
    public bool HasSignerFor(UserId userId)
        => _signers.Exists(s => string.Equals(s.UserId, userId.Value, StringComparison.Ordinal));

    /// <summary>Appends a signature to the order.</summary>
    /// <param name="signer">The subscriber that signed and the signature class used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="signer"/> is <see langword="null"/>.</exception>
    public void AddSignature(VeuSignerView signer)
    {
        ArgumentNullException.ThrowIfNull(signer);
        _signers.Add(signer);
    }

    /// <summary>Computes the digest (SHA-256) over the order data, surfaced by HVD/HVZ/HVS.</summary>
    /// <returns>The SHA-256 hash of <see cref="OrderData"/>.</returns>
    public byte[] ComputeDataDigest() => SHA256.HashData(OrderData);
}
