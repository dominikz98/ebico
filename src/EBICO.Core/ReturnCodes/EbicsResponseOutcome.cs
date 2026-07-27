namespace EBICO.Core.ReturnCodes;

/// <summary>
/// The single effective outcome of an <c>ebicsResponse</c>: the return code that decides the result
/// together with a report text that agrees with it.
/// </summary>
/// <remarks>
/// An EBICS response carries two return-code slots — <c>header/mutable/ReturnCode</c> for technical
/// faults and <c>body/ReturnCode</c> for business faults — while the human-readable
/// <c>header/mutable/ReportText</c> exists only once, in the header. Reading the code from one slot and
/// the text from the other yields contradictions (a business fault reported as <c>EBICS_OK</c>), so both
/// values are resolved together by
/// <see cref="EbicsReturnCodes.CombineOutcome(string?, string?, string?)"/> and travel as one value.
/// </remarks>
/// <param name="Code">The effective six-digit return code; <see cref="EbicsReturnCode.OkCode"/> when neither slot reported a fault.</param>
/// <param name="Text">The report text belonging to <paramref name="Code"/>, or <see langword="null"/> when none is available.</param>
public readonly record struct EbicsResponseOutcome(string Code, string? Text)
{
    /// <summary>Indicates whether <see cref="Code"/> denotes success.</summary>
    public bool IsSuccess => EbicsReturnCodes.IsSuccess(Code);
}
