namespace EBICO.Connector.Upload;

/// <summary>Handles <see cref="CctUploadRequest"/> (SEPA Credit Transfer, <c>CCT</c>).</summary>
internal sealed class CctUploadRequestHandler : PaymentUploadHandlerBase<CctUploadRequest>
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared upload executor.</param>
    public CctUploadRequestHandler(UploadExecutor executor)
        : base(executor)
    {
    }
}

/// <summary>Handles <see cref="CddUploadRequest"/> (SEPA Direct Debit CORE, <c>CDD</c>).</summary>
internal sealed class CddUploadRequestHandler : PaymentUploadHandlerBase<CddUploadRequest>
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared upload executor.</param>
    public CddUploadRequestHandler(UploadExecutor executor)
        : base(executor)
    {
    }
}

/// <summary>Handles <see cref="CdbUploadRequest"/> (SEPA Direct Debit B2B, <c>CDB</c>).</summary>
internal sealed class CdbUploadRequestHandler : PaymentUploadHandlerBase<CdbUploadRequest>
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared upload executor.</param>
    public CdbUploadRequestHandler(UploadExecutor executor)
        : base(executor)
    {
    }
}

/// <summary>Handles <see cref="CipUploadRequest"/> (SEPA Instant Credit Transfer, <c>CIP</c>).</summary>
internal sealed class CipUploadRequestHandler : PaymentUploadHandlerBase<CipUploadRequest>
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="executor">The shared upload executor.</param>
    public CipUploadRequestHandler(UploadExecutor executor)
        : base(executor)
    {
    }
}
