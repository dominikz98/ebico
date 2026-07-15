using System.Globalization;

namespace EBICO.Core.Statements;

/// <summary>
/// The single entry point the server calls to produce ready-to-download content for a resolved statement
/// order type (STA/VMK/C53/C52/C54, issue #40): it generates a deterministic synthetic statement, renders it
/// in the matching format (MT940/MT942/camt.05x) and wraps it in a ZIP container. The returned bytes are the
/// plaintext the download engine then compresses, E002-encrypts and segments.
/// </summary>
public static class StatementContentFactory
{
    /// <summary>
    /// Creates the ZIP-wrapped statement content for <paramref name="orderType"/> and the given subscriber
    /// and reporting period.
    /// </summary>
    /// <param name="orderType">The resolved statement order type (STA/VMK/C53/C52/C54).</param>
    /// <param name="hostId">The bank host id of the requesting subscriber.</param>
    /// <param name="partnerId">The partner id of the requesting subscriber.</param>
    /// <param name="userId">The user id of the requesting subscriber.</param>
    /// <param name="rangeStart">The inclusive first day of the reporting period.</param>
    /// <param name="rangeEnd">The inclusive last day of the reporting period.</param>
    /// <param name="creationTimestamp">The statement creation time (used for camt <c>CreDtTm</c> and the ZIP entry timestamp).</param>
    /// <returns>The ZIP-wrapped statement document as bytes.</returns>
    /// <exception cref="ArgumentException"><paramref name="orderType"/> is not a supported statement order type, or an id/range is invalid.</exception>
    public static byte[] Create(
        string orderType,
        string hostId,
        string partnerId,
        string userId,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DateTimeOffset creationTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderType);

        var statement = SyntheticStatementGenerator.Generate(hostId, partnerId, userId, rangeStart, rangeEnd, creationTimestamp);

        var (content, extension) = orderType switch
        {
            StatementOrderTypes.StatementMt940 => (Mt940Builder.Build(statement), "txt"),
            StatementOrderTypes.InterimReportMt942 => (Mt942Builder.Build(statement), "txt"),
            StatementOrderTypes.StatementCamt053 => (Camt053Builder.Build(statement), "xml"),
            StatementOrderTypes.ReportCamt052 => (Camt052Builder.Build(statement), "xml"),
            StatementOrderTypes.NotificationCamt054 => (Camt054Builder.Build(statement), "xml"),
            _ => throw new ArgumentException($"Unsupported statement order type '{orderType}'.", nameof(orderType)),
        };

        var entryName = $"{orderType}-{rangeEnd.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.{extension}";
        return StatementZipContainer.Wrap(entryName, content, creationTimestamp);
    }
}
