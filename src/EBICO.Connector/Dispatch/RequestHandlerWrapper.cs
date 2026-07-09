using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Connector.Dispatch;

/// <summary>
/// Non-generic bridge that lets <see cref="EbicsClient"/> invoke a handler whose concrete request
/// type is only known at runtime, while the call site knows just <c>IEbicsRequest&lt;TResult&gt;</c>.
/// One wrapper instance is created and cached per concrete request type.
/// </summary>
/// <typeparam name="TResult">The result type of the request.</typeparam>
internal abstract class RequestHandlerWrapper<TResult>
{
    public abstract Task<EbicsResult<TResult>> Handle(
        IEbicsRequest<TResult> request,
        EbicsContext context,
        IServiceProvider services,
        CancellationToken ct);
}

/// <summary>
/// The concrete wrapper bound to a specific request type. It resolves the matching
/// <see cref="IEbicsRequestHandler{TRequest, TResult}"/> from the request scope and invokes it —
/// this is the connector's own dispatch (no MediatR, per ADR-0005).
/// </summary>
/// <typeparam name="TRequest">The concrete request type.</typeparam>
/// <typeparam name="TResult">The result type of the request.</typeparam>
internal sealed class RequestHandlerWrapper<TRequest, TResult> : RequestHandlerWrapper<TResult>
    where TRequest : IEbicsRequest<TResult>
{
    public override Task<EbicsResult<TResult>> Handle(
        IEbicsRequest<TResult> request,
        EbicsContext context,
        IServiceProvider services,
        CancellationToken ct)
    {
        var handler = services.GetService<IEbicsRequestHandler<TRequest, TResult>>()
            ?? throw new EbicsConfigurationException(
                $"No handler is registered for request type '{typeof(TRequest)}'. " +
                $"Register an {typeof(IEbicsRequestHandler<TRequest, TResult>)} in the service collection.");

        return handler.Handle((TRequest)request, context, ct);
    }
}
