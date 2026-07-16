namespace EBICO.Connector.Upload;

/// <summary>
/// Shared base for the SEPA payment convenience handlers: projects an <see cref="IPaymentUploadRequest"/>
/// onto the generic upload inputs and delegates to the shared <see cref="UploadExecutor"/>.
/// </summary>
/// <typeparam name="TRequest">The concrete convenience request type.</typeparam>
internal abstract class PaymentUploadHandlerBase<TRequest> : IEbicsRequestHandler<TRequest, UploadResult>
    where TRequest : class, IEbicsRequest<UploadResult>, IPaymentUploadRequest
{
    private readonly UploadExecutor _executor;

    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared upload executor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="executor"/> is <see langword="null"/>.</exception>
    protected PaymentUploadHandlerBase(UploadExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    /// <inheritdoc />
    public Task<EbicsResult<UploadResult>> Handle(TRequest request, EbicsContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);
        return _executor.ExecuteAsync(
            request.Payload, request.OrderType, btf: null, fileFormat: null, request.MaxSegmentSizeBytes, ctx, ct);
    }
}
