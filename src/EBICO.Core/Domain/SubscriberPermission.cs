namespace EBICO.Core.Domain;

/// <summary>
/// A subscriber's authorisation to act on a given order type with a particular
/// <see cref="SignatureClass"/> — for example, to submit <c>CCT</c> with a transport
/// signature, or to authorise it with a sole signature.
/// </summary>
/// <remarks>
/// The order type is a free-form string at this stage; the typed BTF/order model lands
/// in M5. Construct via <see cref="SubscriberPermission(string, SignatureClass)"/>; the
/// <see langword="default"/> value has a <see langword="null"/> <see cref="OrderType"/>.
/// </remarks>
public readonly record struct SubscriberPermission
{
    /// <summary>Creates a permission for an order type and signature class.</summary>
    /// <param name="orderType">The order/BTF type (e.g. <c>"CCT"</c>, <c>"STA"</c>).</param>
    /// <param name="signatureClass">The signature class the subscriber is authorised for.</param>
    /// <exception cref="ArgumentException"><paramref name="orderType"/> is null, empty or whitespace.</exception>
    public SubscriberPermission(string orderType, SignatureClass signatureClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderType);
        OrderType = orderType;
        SignatureClass = signatureClass;
    }

    /// <summary>The order/BTF type this permission applies to (e.g. <c>"CCT"</c>, <c>"STA"</c>).</summary>
    public string OrderType { get; }

    /// <summary>The signature class the subscriber is authorised for on <see cref="OrderType"/>.</summary>
    public SignatureClass SignatureClass { get; }

    /// <summary>
    /// Indicates whether this permission is transport-only (i.e. carries no authorising
    /// signature). Shorthand for <c>SignatureClass.IsTransportOnly()</c>.
    /// </summary>
    public bool IsTransportOnly => SignatureClass.IsTransportOnly();
}
