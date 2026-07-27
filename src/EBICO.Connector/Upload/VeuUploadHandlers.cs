namespace EBICO.Connector.Upload;

/// <summary>
/// Shared base for the VEU upload convenience handlers: projects an <see cref="IVeuUploadRequest"/> onto
/// the generic upload inputs — including the referenced parked order — and delegates to the shared
/// <see cref="UploadExecutor"/>.
/// </summary>
/// <typeparam name="TRequest">The concrete convenience request type.</typeparam>
internal abstract class VeuUploadHandlerBase<TRequest> : IEbicsRequestHandler<TRequest, UploadResult>
    where TRequest : class, IEbicsRequest<UploadResult>, IVeuUploadRequest
{
    private readonly UploadExecutor _executor;

    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared upload executor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="executor"/> is <see langword="null"/>.</exception>
    protected VeuUploadHandlerBase(UploadExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    /// <inheritdoc />
    public Task<EbicsResult<UploadResult>> Handle(TRequest request, EbicsContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);

        // HVE/HVS are administrative order types: no BTF even on H005 (the executor keeps them as the
        // AdminOrderType) and never a distributed-signature flag of their own.
        return _executor.ExecuteAsync(
            request.Payload,
            request.OrderType,
            btf: null,
            fileFormat: null,
            request.MaxSegmentSizeBytes,
            ctx,
            ct,
            distributedSignature: false,
            veu: request.Order);
    }
}

/// <summary>Handles <see cref="HveUploadRequest"/> (add a distributed signature, <c>HVE</c>).</summary>
internal sealed class HveUploadRequestHandler : VeuUploadHandlerBase<HveUploadRequest>
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared upload executor.</param>
    public HveUploadRequestHandler(UploadExecutor executor)
        : base(executor)
    {
    }
}

/// <summary>Handles <see cref="HvsUploadRequest"/> (cancel a parked order, <c>HVS</c>).</summary>
internal sealed class HvsUploadRequestHandler : VeuUploadHandlerBase<HvsUploadRequest>
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared upload executor.</param>
    public HvsUploadRequestHandler(UploadExecutor executor)
        : base(executor)
    {
    }
}
