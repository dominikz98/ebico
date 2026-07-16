namespace EBICO.Connector.Download;

/// <summary>
/// Shared base for the download convenience handlers: projects an <see cref="IDownloadConvenienceRequest"/>
/// onto the generic download inputs and delegates to the shared <see cref="DownloadExecutor"/>.
/// </summary>
/// <typeparam name="TRequest">The concrete convenience request type.</typeparam>
internal abstract class DownloadConvenienceHandlerBase<TRequest> : IEbicsRequestHandler<TRequest, DownloadResult>
    where TRequest : class, IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    private readonly DownloadExecutor _executor;

    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared download executor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="executor"/> is <see langword="null"/>.</exception>
    protected DownloadConvenienceHandlerBase(DownloadExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    /// <inheritdoc />
    public Task<EbicsResult<DownloadResult>> Handle(TRequest request, EbicsContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);
        return _executor.ExecuteAsync(
            request.OrderType, btf: null, fileFormat: null, request.Period, request.Parse, ctx, ct);
    }
}
