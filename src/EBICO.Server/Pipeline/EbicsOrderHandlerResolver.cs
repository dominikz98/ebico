using EBICO.Core;

namespace EBICO.Server.Pipeline;

/// <summary>
/// Default <see cref="IEbicsOrderHandlerResolver"/>: matches the registered
/// <see cref="IEbicsOrderHandler"/>s by (version, order type). In the skeleton the set of handlers
/// is empty, so <see cref="Resolve"/> always returns <see langword="null"/>.
/// </summary>
public sealed class EbicsOrderHandlerResolver : IEbicsOrderHandlerResolver
{
    private readonly IReadOnlyList<IEbicsOrderHandler> _handlers;

    /// <summary>Initializes the resolver with the registered handlers.</summary>
    /// <param name="handlers">The registered order handlers (may be empty).</param>
    public EbicsOrderHandlerResolver(IEnumerable<IEbicsOrderHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToArray();
    }

    /// <inheritdoc />
    public IEbicsOrderHandler? Resolve(EbicsVersion version, string? orderType)
    {
        if (string.IsNullOrEmpty(orderType))
        {
            return null;
        }

        foreach (var handler in _handlers)
        {
            if (handler.Version == version && string.Equals(handler.OrderType, orderType, StringComparison.Ordinal))
            {
                return handler;
            }
        }

        return null;
    }
}
