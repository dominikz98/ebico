namespace EBICO.Connector.Upload;

/// <summary>Handles the generic <see cref="UploadRequest"/> by delegating to the shared <see cref="UploadExecutor"/>.</summary>
internal sealed class UploadRequestHandler : IEbicsRequestHandler<UploadRequest, UploadResult>
{
    private readonly UploadExecutor _executor;

    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared upload executor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="executor"/> is <see langword="null"/>.</exception>
    public UploadRequestHandler(UploadExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    /// <inheritdoc />
    public Task<EbicsResult<UploadResult>> Handle(UploadRequest request, EbicsContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);
        return _executor.ExecuteAsync(
            request.OrderData, request.OrderType, request.Btf, request.FileFormat, request.MaxSegmentSizeBytes, ctx, ct);
    }
}
