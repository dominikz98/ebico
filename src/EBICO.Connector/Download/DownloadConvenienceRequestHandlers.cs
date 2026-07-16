namespace EBICO.Connector.Download;

/// <summary>Handles <see cref="StaDownloadRequest"/> (account statement MT940, <c>STA</c>).</summary>
internal sealed class StaDownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<StaDownloadRequest>(executor);

/// <summary>Handles <see cref="VmkDownloadRequest"/> (interim transaction report MT942, <c>VMK</c>).</summary>
internal sealed class VmkDownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<VmkDownloadRequest>(executor);

/// <summary>Handles <see cref="C53DownloadRequest"/> (bank-to-customer statement camt.053, <c>C53</c>).</summary>
internal sealed class C53DownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<C53DownloadRequest>(executor);

/// <summary>Handles <see cref="C52DownloadRequest"/> (bank-to-customer account report camt.052, <c>C52</c>).</summary>
internal sealed class C52DownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<C52DownloadRequest>(executor);

/// <summary>Handles <see cref="C54DownloadRequest"/> (bank-to-customer debit/credit notification camt.054, <c>C54</c>).</summary>
internal sealed class C54DownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<C54DownloadRequest>(executor);

/// <summary>Handles <see cref="HtdDownloadRequest"/> (customer and subscriber data, <c>HTD</c>).</summary>
internal sealed class HtdDownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<HtdDownloadRequest>(executor);

/// <summary>Handles <see cref="HkdDownloadRequest"/> (customer data including subscribers, <c>HKD</c>).</summary>
internal sealed class HkdDownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<HkdDownloadRequest>(executor);

/// <summary>Handles <see cref="HaaDownloadRequest"/> (available order types, <c>HAA</c>).</summary>
internal sealed class HaaDownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<HaaDownloadRequest>(executor);

/// <summary>Handles <see cref="HpdDownloadRequest"/> (bank parameters, <c>HPD</c>).</summary>
internal sealed class HpdDownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<HpdDownloadRequest>(executor);

/// <summary>Handles <see cref="HacDownloadRequest"/> (machine-readable customer protocol, <c>HAC</c>).</summary>
internal sealed class HacDownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<HacDownloadRequest>(executor);

/// <summary>Handles <see cref="PtkDownloadRequest"/> (textual customer protocol, <c>PTK</c>).</summary>
internal sealed class PtkDownloadRequestHandler(DownloadExecutor executor) : DownloadConvenienceHandlerBase<PtkDownloadRequest>(executor);
