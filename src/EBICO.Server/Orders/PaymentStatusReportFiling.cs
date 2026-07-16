using EBICO.Core.Payments;
using EBICO.Server.State;

namespace EBICO.Server.Orders;

/// <summary>
/// Files the positive <c>pain.002</c> customer payment status report for an accepted payment order into the
/// download-data provider for later delivery (the "Ablage zur späteren Auslieferung"). Shared between the
/// immediate-accept path of <see cref="SepaPaymentUploadProcessor"/> (issue #39) and the release path of
/// <see cref="VeuSignatureUploadProcessor"/> (issue #42), which files the same report once an order awaiting
/// distributed signatures has collected all required signatures.
/// </summary>
internal static class PaymentStatusReportFiling
{
    /// <summary>
    /// Builds a positive <c>pain.002</c> status report echoing the original message identifiers and enqueues
    /// it under <paramref name="statusReportOrderType"/> for <paramref name="subscriber"/>.
    /// </summary>
    /// <param name="downloadDataProvider">The provider the report is filed into.</param>
    /// <param name="statusReportOrderType">The download order type the report is filed under (e.g. <c>"PSR"</c>).</param>
    /// <param name="subscriber">The subscriber the report is made available to.</param>
    /// <param name="messageId">The original message id (<c>GrpHdr/MsgId</c>) to echo.</param>
    /// <param name="messageNameId">The original ISO message-name id (e.g. <c>"pain.001.001.09"</c>) to echo.</param>
    /// <param name="now">The report creation timestamp.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the report has been filed.</returns>
    public static Task FileAsync(
        IDownloadDataProvider downloadDataProvider,
        string statusReportOrderType,
        SubscriberKeyRef subscriber,
        string messageId,
        string messageNameId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var reportMessageId = "PSR-" + Guid.NewGuid().ToString("N");
        var statusReport = PainStatusReportBuilder.Build(messageId, messageNameId, reportMessageId, now);
        return downloadDataProvider.EnqueueAsync(subscriber, statusReportOrderType, statusReport, ct);
    }
}
