using EBICO.Core;

namespace EBICO.Server.Pipeline;

/// <summary>
/// Resolves the <see cref="IEbicsOrderHandler"/> registered for a given protocol version and
/// order type, if any.
/// </summary>
public interface IEbicsOrderHandlerResolver
{
    /// <summary>
    /// Returns the handler for (<paramref name="version"/>, <paramref name="orderType"/>), or
    /// <see langword="null"/> when none is registered (or <paramref name="orderType"/> is empty).
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="orderType">The order type, or <see langword="null"/>.</param>
    /// <returns>The matching handler, or <see langword="null"/>.</returns>
    IEbicsOrderHandler? Resolve(EbicsVersion version, string? orderType);
}
