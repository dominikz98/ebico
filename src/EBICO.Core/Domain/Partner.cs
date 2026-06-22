namespace EBICO.Core.Domain;

/// <summary>
/// A partner (Kunde / customer) at a bank, identified by its <see cref="PartnerId"/>. A
/// partner groups one or more <see cref="Subscriber"/>s. Lightweight identity-and-metadata
/// aggregate; richer master data is added by the server layer (M3).
/// </summary>
public sealed class Partner
{
    /// <summary>Creates a partner.</summary>
    /// <param name="partnerId">The partner's EBICS identifier.</param>
    /// <param name="name">Optional human-readable name.</param>
    public Partner(PartnerId partnerId, string? name = null)
    {
        PartnerId = partnerId;
        Name = name;
    }

    /// <summary>The partner's EBICS identifier (<c>PartnerID</c>).</summary>
    public PartnerId PartnerId { get; }

    /// <summary>Optional human-readable name of the partner.</summary>
    public string? Name { get; }
}
