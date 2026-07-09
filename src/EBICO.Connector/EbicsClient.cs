using System.Collections.Concurrent;
using EBICO.Connector.Configuration;
using EBICO.Connector.Dispatch;
using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Connector;

/// <summary>
/// The default <see cref="IEbicsClient"/>. It dispatches each request to its registered handler
/// using cached, per-request-type wrappers (the connector's own dispatch — no MediatR, ADR-0005)
/// and builds a fresh <see cref="EbicsContext"/> from a per-send DI scope.
/// </summary>
internal sealed class EbicsClient : IEbicsClient
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<Type, object> _wrappers = new();

    public EbicsClient(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public Task<EbicsResult<TResult>> Send<TResult>(IEbicsRequest<TResult> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Reflection is confined to first-seen request types; the hot path is a cached virtual call.
        var wrapper = (RequestHandlerWrapper<TResult>)_wrappers.GetOrAdd(
            request.GetType(),
            static (requestType, resultType) => Activator.CreateInstance(
                typeof(RequestHandlerWrapper<,>).MakeGenericType(requestType, resultType))!,
            typeof(TResult));

        return SendCore(wrapper, request, ct);
    }

    private async Task<EbicsResult<TResult>> SendCore<TResult>(
        RequestHandlerWrapper<TResult> wrapper,
        IEbicsRequest<TResult> request,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        var connection = services.GetRequiredService<EbicsConnection>();
        var keys = services.GetRequiredService<IKeyStore>();
        var transport = services.GetRequiredService<ITransport>();
        var context = new EbicsContext(connection, keys, transport);

        return await wrapper.Handle(request, context, services, ct).ConfigureAwait(false);
    }
}
