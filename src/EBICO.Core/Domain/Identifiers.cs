namespace EBICO.Core.Domain;

/// <summary>
/// The EBICS host identifier (<c>HostID</c>): the bank/server endpoint a subscriber
/// connects to, administered on the server side.
/// </summary>
/// <remarks>
/// Obtain instances via <see cref="Create(string)"/> or
/// <see cref="TryCreate(string?, out HostId)"/>. The <see langword="default"/> value
/// (<c>default(HostId)</c> / <c>new HostId()</c>) is <b>not</b> a valid identifier and
/// has a <see langword="null"/> <see cref="Value"/>.
/// </remarks>
public readonly record struct HostId
{
    private HostId(string value) => Value = value;

    /// <summary>The validated identifier string (1–35 chars matching <c>[a-zA-Z0-9,=]</c>).</summary>
    public string Value { get; }

    /// <summary>Creates a validated <see cref="HostId"/>.</summary>
    /// <param name="value">The raw identifier.</param>
    /// <returns>The validated <see cref="HostId"/>.</returns>
    /// <exception cref="InvalidEbicsIdentifierException"><paramref name="value"/> is not a valid EBICS identifier.</exception>
    public static HostId Create(string value) => new(EbicsIdentifier.Validate(value, nameof(HostId)));

    /// <summary>Tries to create a validated <see cref="HostId"/> without throwing.</summary>
    /// <param name="value">The raw identifier.</param>
    /// <param name="id">The validated identifier when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a valid EBICS identifier.</returns>
    public static bool TryCreate(string? value, out HostId id)
    {
        if (EbicsIdentifier.IsValid(value))
        {
            id = new HostId(value);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Returns the underlying identifier string.</summary>
    /// <returns>The <see cref="Value"/>.</returns>
    public override string ToString() => Value;
}

/// <summary>
/// The EBICS partner identifier (<c>PartnerID</c>): the customer (Kunde) a subscriber
/// belongs to, administered on the server side.
/// </summary>
/// <remarks>
/// Obtain instances via <see cref="Create(string)"/> or
/// <see cref="TryCreate(string?, out PartnerId)"/>. The <see langword="default"/> value is
/// not a valid identifier and has a <see langword="null"/> <see cref="Value"/>.
/// </remarks>
public readonly record struct PartnerId
{
    private PartnerId(string value) => Value = value;

    /// <summary>The validated identifier string (1–35 chars matching <c>[a-zA-Z0-9,=]</c>).</summary>
    public string Value { get; }

    /// <summary>Creates a validated <see cref="PartnerId"/>.</summary>
    /// <param name="value">The raw identifier.</param>
    /// <returns>The validated <see cref="PartnerId"/>.</returns>
    /// <exception cref="InvalidEbicsIdentifierException"><paramref name="value"/> is not a valid EBICS identifier.</exception>
    public static PartnerId Create(string value) => new(EbicsIdentifier.Validate(value, nameof(PartnerId)));

    /// <summary>Tries to create a validated <see cref="PartnerId"/> without throwing.</summary>
    /// <param name="value">The raw identifier.</param>
    /// <param name="id">The validated identifier when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a valid EBICS identifier.</returns>
    public static bool TryCreate(string? value, out PartnerId id)
    {
        if (EbicsIdentifier.IsValid(value))
        {
            id = new PartnerId(value);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Returns the underlying identifier string.</summary>
    /// <returns>The <see cref="Value"/>.</returns>
    public override string ToString() => Value;
}

/// <summary>
/// The EBICS user identifier (<c>UserID</c>): the user (Teilnehmer) assigned to a given
/// customer, administered on the server side.
/// </summary>
/// <remarks>
/// Obtain instances via <see cref="Create(string)"/> or
/// <see cref="TryCreate(string?, out UserId)"/>. The <see langword="default"/> value is
/// not a valid identifier and has a <see langword="null"/> <see cref="Value"/>.
/// </remarks>
public readonly record struct UserId
{
    private UserId(string value) => Value = value;

    /// <summary>The validated identifier string (1–35 chars matching <c>[a-zA-Z0-9,=]</c>).</summary>
    public string Value { get; }

    /// <summary>Creates a validated <see cref="UserId"/>.</summary>
    /// <param name="value">The raw identifier.</param>
    /// <returns>The validated <see cref="UserId"/>.</returns>
    /// <exception cref="InvalidEbicsIdentifierException"><paramref name="value"/> is not a valid EBICS identifier.</exception>
    public static UserId Create(string value) => new(EbicsIdentifier.Validate(value, nameof(UserId)));

    /// <summary>Tries to create a validated <see cref="UserId"/> without throwing.</summary>
    /// <param name="value">The raw identifier.</param>
    /// <param name="id">The validated identifier when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a valid EBICS identifier.</returns>
    public static bool TryCreate(string? value, out UserId id)
    {
        if (EbicsIdentifier.IsValid(value))
        {
            id = new UserId(value);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Returns the underlying identifier string.</summary>
    /// <returns>The <see cref="Value"/>.</returns>
    public override string ToString() => Value;
}

/// <summary>
/// The EBICS system identifier (<c>SystemID</c>): identifies the technical system on
/// multi-user setups. Optional — only present when a subscriber is a technical user.
/// </summary>
/// <remarks>
/// Obtain instances via <see cref="Create(string)"/> or
/// <see cref="TryCreate(string?, out SystemId)"/>. The <see langword="default"/> value is
/// not a valid identifier and has a <see langword="null"/> <see cref="Value"/>.
/// </remarks>
public readonly record struct SystemId
{
    private SystemId(string value) => Value = value;

    /// <summary>The validated identifier string (1–35 chars matching <c>[a-zA-Z0-9,=]</c>).</summary>
    public string Value { get; }

    /// <summary>Creates a validated <see cref="SystemId"/>.</summary>
    /// <param name="value">The raw identifier.</param>
    /// <returns>The validated <see cref="SystemId"/>.</returns>
    /// <exception cref="InvalidEbicsIdentifierException"><paramref name="value"/> is not a valid EBICS identifier.</exception>
    public static SystemId Create(string value) => new(EbicsIdentifier.Validate(value, nameof(SystemId)));

    /// <summary>Tries to create a validated <see cref="SystemId"/> without throwing.</summary>
    /// <param name="value">The raw identifier.</param>
    /// <param name="id">The validated identifier when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a valid EBICS identifier.</returns>
    public static bool TryCreate(string? value, out SystemId id)
    {
        if (EbicsIdentifier.IsValid(value))
        {
            id = new SystemId(value);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Returns the underlying identifier string.</summary>
    /// <returns>The <see cref="Value"/>.</returns>
    public override string ToString() => Value;
}
