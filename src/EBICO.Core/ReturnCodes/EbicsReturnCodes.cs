namespace EBICO.Core.ReturnCodes;

/// <summary>
/// The single source of truth for the known EBICS return codes (see <see cref="EbicsReturnCode"/>),
/// providing lookup by six-digit code. Mirrors the <c>KeyVersions</c> registry style.
/// </summary>
/// <remarks>
/// The catalogue reflects EBICS Annex 1; entries beyond the nine codes used by the running code are
/// provided for completeness and flagged with <c>⚠️ Spec-Vorbehalt</c> on their <see cref="EbicsReturnCode"/>
/// field, to be verified against the official annexes.
/// </remarks>
public static class EbicsReturnCodes
{
    private static readonly EbicsReturnCode[] AllCodes =
    [
        // Technical (header)
        EbicsReturnCode.Ok,
        EbicsReturnCode.DownloadPostprocessDone,
        EbicsReturnCode.DownloadPostprocessSkipped,
        EbicsReturnCode.TxSegmentNumberUnderrun,
        EbicsReturnCode.OrderParamsIgnored,
        EbicsReturnCode.AuthenticationFailed,
        EbicsReturnCode.InvalidRequest,
        EbicsReturnCode.InternalError,
        EbicsReturnCode.TxRecoverySync,

        // Business (body)
        EbicsReturnCode.AuthorisationOrderTypeFailed,
        EbicsReturnCode.InvalidOrderDataFormat,
        EbicsReturnCode.NoDownloadDataAvailable,
        EbicsReturnCode.InvalidUserOrUserState,
        EbicsReturnCode.UserUnknown,
        EbicsReturnCode.InvalidUserState,
        EbicsReturnCode.InvalidOrderType,
        EbicsReturnCode.UnsupportedOrderType,
        EbicsReturnCode.BankPubkeyUpdateRequired,
        EbicsReturnCode.SegmentSizeExceeded,
        EbicsReturnCode.InvalidXml,
        EbicsReturnCode.InvalidHostId,
        EbicsReturnCode.TxUnknownTxid,
        EbicsReturnCode.TxAbort,
        EbicsReturnCode.TxMessageReplay,
        EbicsReturnCode.TxSegmentNumberExceeded,
        EbicsReturnCode.InvalidRequestContent,
        EbicsReturnCode.MaxOrderDataSizeExceeded,
        EbicsReturnCode.MaxSegmentsExceeded,
        EbicsReturnCode.MaxTransactionsExceeded,
        EbicsReturnCode.PartnerIdMismatch,
        EbicsReturnCode.IncompatibleOrderAttribute,
        EbicsReturnCode.InvalidOrderIdentifier,
    ];

    /// <summary>All known return codes, technical (header) first, then business (body).</summary>
    public static IReadOnlyList<EbicsReturnCode> All => AllCodes;

    /// <summary>Indicates whether a six-digit code denotes success (<see cref="EbicsReturnCode.OkCode"/>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when <paramref name="code"/> equals <see cref="EbicsReturnCode.OkCode"/>.</returns>
    public static bool IsSuccess(string? code) => string.Equals(code, EbicsReturnCode.OkCode, StringComparison.Ordinal);

    /// <summary>
    /// Reduces the two return-code slots of an <c>ebicsResponse</c> to the single effective outcome a
    /// caller should see: technical codes live in <c>header/mutable/ReturnCode</c>, business codes in
    /// <c>body/ReturnCode</c>, and the unused slot carries <see cref="EbicsReturnCode.OkCode"/>. The
    /// non-OK slot wins — the header first, since a technical fault supersedes the body.
    /// </summary>
    /// <remarks>
    /// The report text is resolved <b>from the same slot as the winning code</b>. That is the point of
    /// this helper: <c>ReportText</c> only ever exists in the header, so pairing a body code with the
    /// header's text produced contradictions like <c>090005</c> reported as <c>EBICS_OK</c> (#124). When
    /// the body wins, the text therefore comes from this registry's symbolic name for that code, or is
    /// <see langword="null"/> for a code the catalogue does not know.
    /// </remarks>
    /// <param name="headerCode">The <c>header/mutable/ReturnCode</c> value, if any.</param>
    /// <param name="headerText">The <c>header/mutable/ReportText</c> value, if any.</param>
    /// <param name="bodyCode">The <c>body/ReturnCode</c> value, if any.</param>
    /// <returns>The effective code paired with a report text that agrees with it.</returns>
    public static EbicsResponseOutcome CombineOutcome(string? headerCode, string? headerText, string? bodyCode)
    {
        if (!string.IsNullOrEmpty(headerCode) && !IsSuccess(headerCode))
        {
            return new EbicsResponseOutcome(headerCode, headerText ?? SymbolicNameOrNull(headerCode));
        }

        if (!string.IsNullOrEmpty(bodyCode) && !IsSuccess(bodyCode))
        {
            // Deliberately NOT headerText — that slot says EBICS_OK whenever the body carries the fault.
            return new EbicsResponseOutcome(bodyCode, SymbolicNameOrNull(bodyCode));
        }

        return new EbicsResponseOutcome(EbicsReturnCode.OkCode, headerText);
    }

    private static string? SymbolicNameOrNull(string code)
        => TryFromCode(code, out var known) ? known.SymbolicName : null;

    /// <summary>Returns the catalogue entry for a known six-digit return code.</summary>
    /// <param name="code">The six-digit code (e.g. <c>"091010"</c>).</param>
    /// <returns>The matching <see cref="EbicsReturnCode"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="code"/> is not a known return code.</exception>
    public static EbicsReturnCode Get(string code)
    {
        if (TryFromCode(code, out var returnCode))
        {
            return returnCode;
        }

        throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown EBICS return code.");
    }

    /// <summary>Tries to resolve a six-digit return code to its catalogue entry without throwing.</summary>
    /// <param name="code">The code to resolve.</param>
    /// <param name="returnCode">The matching entry when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="code"/> is a known return code.</returns>
    public static bool TryFromCode(string? code, out EbicsReturnCode returnCode)
    {
        foreach (var candidate in AllCodes)
        {
            if (string.Equals(candidate.Code, code, StringComparison.Ordinal))
            {
                returnCode = candidate;
                return true;
            }
        }

        returnCode = default;
        return false;
    }
}
