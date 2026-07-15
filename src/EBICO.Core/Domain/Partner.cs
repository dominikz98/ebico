namespace EBICO.Core.Domain;

/// <summary>
/// A partner (Kunde / customer) at a bank, identified within a bank by its <see cref="PartnerId"/>.
/// A partner belongs to exactly one bank (<see cref="HostId"/>) and groups one or more
/// <see cref="Subscriber"/>s. Lightweight identity-and-metadata aggregate.
/// </summary>
/// <remarks>
/// The partner is scoped to a bank: the same <see cref="PartnerId"/> may denote different
/// customers at different banks, so the identity is the (<see cref="HostId"/>,
/// <see cref="PartnerId"/>) pair. This makes the emulator multi-tenant (Mehr-Mandanten-Fähigkeit).
/// The optional <see cref="Address"/> and <see cref="Accounts"/> feed the customer/subscriber data
/// download orders HTD/HKD (issue #41).
/// </remarks>
public sealed class Partner
{
    private readonly BankAccount[] _accounts;

    /// <summary>Creates a partner belonging to a bank.</summary>
    /// <param name="hostId">The host identifier of the bank the partner belongs to.</param>
    /// <param name="partnerId">The partner's EBICS identifier, unique within the bank.</param>
    /// <param name="name">Optional human-readable name.</param>
    /// <param name="address">Optional postal address (surfaced by HTD/HKD).</param>
    /// <param name="accounts">Optional bank accounts held by the partner (surfaced by HTD/HKD).</param>
    public Partner(
        HostId hostId,
        PartnerId partnerId,
        string? name = null,
        Address? address = null,
        IEnumerable<BankAccount>? accounts = null)
    {
        HostId = hostId;
        PartnerId = partnerId;
        Name = name;
        Address = address;
        _accounts = accounts?.ToArray() ?? [];
    }

    /// <summary>The host identifier (<c>HostID</c>) of the bank the partner belongs to.</summary>
    public HostId HostId { get; }

    /// <summary>The partner's EBICS identifier (<c>PartnerID</c>), unique within the bank.</summary>
    public PartnerId PartnerId { get; }

    /// <summary>Optional human-readable name of the partner.</summary>
    public string? Name { get; }

    /// <summary>The partner's optional postal address (surfaced by HTD/HKD), or <see langword="null"/>.</summary>
    public Address? Address { get; }

    /// <summary>The bank accounts held by the partner (surfaced by HTD/HKD); possibly empty.</summary>
    public IReadOnlyCollection<BankAccount> Accounts => _accounts;
}
