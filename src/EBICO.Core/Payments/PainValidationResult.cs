namespace EBICO.Core.Payments;

/// <summary>
/// The outcome of validating an uploaded SEPA <c>pain</c> payload (see <see cref="SepaPaymentValidator"/>).
/// On success it also carries the two identifiers needed to build the <c>pain.002</c> status report
/// (<see cref="MessageId"/> and <see cref="MessageNameId"/>); on failure it carries the human-readable
/// <see cref="Errors"/>.
/// </summary>
/// <param name="IsValid">Whether the payload passed structural/semantic validation.</param>
/// <param name="Errors">The validation errors when <see cref="IsValid"/> is <see langword="false"/>; empty otherwise.</param>
/// <param name="MessageId">The original <c>GrpHdr/MsgId</c> when valid; otherwise <see langword="null"/>.</param>
/// <param name="MessageNameId">The original ISO message-name id (e.g. <c>"pain.001.001.09"</c>, taken from the document namespace) when valid; otherwise <see langword="null"/>.</param>
public sealed record PainValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    string? MessageId,
    string? MessageNameId)
{
    /// <summary>Creates a successful result carrying the identifiers extracted from the payload.</summary>
    /// <param name="messageId">The original <c>GrpHdr/MsgId</c>.</param>
    /// <param name="messageNameId">The original ISO message-name id (e.g. <c>"pain.001.001.09"</c>).</param>
    /// <returns>A valid result.</returns>
    public static PainValidationResult Valid(string messageId, string messageNameId)
        => new(true, [], messageId, messageNameId);

    /// <summary>Creates a failed result carrying the collected <paramref name="errors"/>.</summary>
    /// <param name="errors">The validation errors (at least one).</param>
    /// <returns>An invalid result.</returns>
    public static PainValidationResult Invalid(IReadOnlyList<string> errors)
        => new(false, errors, null, null);
}
