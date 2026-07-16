namespace EBICO.Connector.Download;

/// <summary>Handles the generic <see cref="DownloadRequest"/> by delegating to the shared <see cref="DownloadExecutor"/>.</summary>
internal sealed class DownloadRequestHandler : IEbicsRequestHandler<DownloadRequest, DownloadResult>
{
    private readonly DownloadExecutor _executor;

    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared download executor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="executor"/> is <see langword="null"/>.</exception>
    public DownloadRequestHandler(DownloadExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    /// <inheritdoc />
    public Task<EbicsResult<DownloadResult>> Handle(DownloadRequest request, EbicsContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);
        return _executor.ExecuteAsync(
            request.OrderType, request.Btf, request.FileFormat, request.Period, request.Parse, ctx, ct);
    }
}
